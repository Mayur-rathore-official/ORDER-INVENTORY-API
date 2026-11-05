export type Role = 'Admin' | 'Customer'

export interface User {
  id: string
  fullName: string
  email: string
  role: Role
}

export interface AuthResponse {
  accessToken: string
  refreshToken: string
  accessTokenExpiresAtUtc: string
  user: User
}

export interface ProblemDetails {
  title?: string
  detail?: string
  status?: number
}

export interface PagedResult<T> {
  items: T[]
  page: number
  pageSize: number
  totalCount: number
}

export interface Product {
  id: string
  sku: string
  name: string
  price: number
  stockQuantity: number
  isActive: boolean
  rowVersion: string
}

export interface ProductQuery {
  page: number
  pageSize: number
  name: string
  minPrice: string
  maxPrice: string
  inStock: '' | 'true' | 'false'
  sortBy: 'name' | 'price' | 'sku'
  sortDir: 'asc' | 'desc'
}

export interface OrderItem {
  id: string
  productId: string
  productName: string
  sku: string
  quantity: number
  unitPrice: number
}

export interface Order {
  id: string
  customerId: string
  customerName: string
  status: 'Pending' | 'Confirmed' | 'Shipped' | 'Cancelled'
  subTotal: number
  discount: number
  total: number
  createdAt: string
  items: OrderItem[]
}

export interface TopCustomer {
  customerId: string
  fullName: string
  email: string
  totalSpend: number
  orderCount: number
}

export interface CartLine {
  productId: string
  sku: string
  name: string
  price: number
  quantity: number
  stockQuantity: number
}
