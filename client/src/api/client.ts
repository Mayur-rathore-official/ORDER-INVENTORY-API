import type { AuthResponse, Order, PagedResult, Product, TopCustomer } from '../types'
import { loadSession, saveSession } from '../auth/session'
import { ApiError, readProblem } from '../lib/problem'

type Method = 'GET' | 'POST' | 'PUT' | 'PATCH'

interface RequestOptions {
  method?: Method
  body?: unknown
  idempotencyKey?: string
  auth?: boolean
}

let refreshInFlight: Promise<boolean> | null = null

async function refreshAccessToken(): Promise<boolean> {
  const session = loadSession()
  if (!session?.refreshToken) return false

  const response = await fetch('/api/auth/refresh', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ refreshToken: session.refreshToken }),
  })

  if (!response.ok) {
    saveSession(null)
    window.dispatchEvent(new Event('oi:logout'))
    return false
  }

  const next = (await response.json()) as AuthResponse
  saveSession(next)
  window.dispatchEvent(new Event('oi:session'))
  return true
}

export async function api<T>(path: string, options: RequestOptions = {}, allowRefresh = true): Promise<T> {
  const headers = new Headers()
  if (options.body !== undefined) headers.set('Content-Type', 'application/json')
  if (options.idempotencyKey) headers.set('Idempotency-Key', options.idempotencyKey)

  const session = loadSession()
  if (options.auth !== false && session?.accessToken) {
    headers.set('Authorization', `Bearer ${session.accessToken}`)
  }

  const response = await fetch(path, {
    method: options.method ?? 'GET',
    headers,
    body: options.body === undefined ? undefined : JSON.stringify(options.body),
  })

  if (response.status === 401 && allowRefresh && options.auth !== false && !path.startsWith('/api/auth/')) {
    refreshInFlight ??= refreshAccessToken().finally(() => {
      refreshInFlight = null
    })
    const refreshed = await refreshInFlight
    if (refreshed) return api<T>(path, options, false)
  }

  if (!response.ok) throw await readProblem(response)
  if (response.status === 204) return undefined as T
  return (await response.json()) as T
}

export const authApi = {
  login: (email: string, password: string) =>
    api<AuthResponse>('/api/auth/login', { method: 'POST', body: { email, password }, auth: false }, false),
  register: (fullName: string, email: string, password: string) =>
    api<AuthResponse>('/api/auth/register', { method: 'POST', body: { fullName, email, password }, auth: false }, false),
}

export const productApi = {
  list: (query: string) => api<PagedResult<Product>>(`/api/products?${query}`, { auth: false }),
  get: (id: string) => api<Product>(`/api/products/${id}`),
  create: (body: { sku: string; name: string; price: number; stockQuantity: number; isActive: boolean }) =>
    api<Product>('/api/products', { method: 'POST', body }),
  update: (
    id: string,
    body: { sku: string; name: string; price: number; stockQuantity: number; isActive: boolean; rowVersion: string },
  ) => api<Product>(`/api/products/${id}`, { method: 'PUT', body }),
}

export const orderApi = {
  place: (items: { productId: string; quantity: number }[], idempotencyKey: string) =>
    api<Order>('/api/orders', { method: 'POST', body: { items }, idempotencyKey }),
  mine: (page: number, status?: string) => {
    const params = new URLSearchParams({ page: String(page), pageSize: '10' })
    if (status) params.set('status', status)
    return api<PagedResult<Order>>(`/api/orders/my?${params}`)
  },
  list: (page: number, status?: string) => {
    const params = new URLSearchParams({ page: String(page), pageSize: '10' })
    if (status) params.set('status', status)
    return api<PagedResult<Order>>(`/api/orders?${params}`)
  },
  changeStatus: (id: string, status: string) =>
    api<Order>(`/api/orders/${id}/status`, { method: 'PATCH', body: { status } }),
}

export const reportApi = {
  topCustomers: (days: number) => api<TopCustomer[]>(`/api/reports/top-customers?days=${days}`),
}

export function isApiError(error: unknown): error is ApiError {
  return error instanceof ApiError
}
