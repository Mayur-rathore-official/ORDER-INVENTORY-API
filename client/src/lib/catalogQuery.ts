import type { ProductQuery } from '../types'

export const defaultProductQuery: ProductQuery = {
  page: 1,
  pageSize: 8,
  name: '',
  minPrice: '',
  maxPrice: '',
  inStock: '',
  sortBy: 'name',
  sortDir: 'asc',
}

export function parseProductQuery(params: URLSearchParams): ProductQuery {
  const page = Number(params.get('page') ?? '1')
  const sortBy = params.get('sortBy')
  const sortDir = params.get('sortDir')
  const inStock = params.get('inStock')
  return {
    page: Number.isInteger(page) && page > 0 ? page : 1,
    pageSize: defaultProductQuery.pageSize,
    name: params.get('name') ?? '',
    minPrice: params.get('minPrice') ?? '',
    maxPrice: params.get('maxPrice') ?? '',
    inStock: inStock === 'true' || inStock === 'false' ? inStock : '',
    sortBy: sortBy === 'price' || sortBy === 'sku' || sortBy === 'name' ? sortBy : 'name',
    sortDir: sortDir === 'desc' ? 'desc' : 'asc',
  }
}

export function toSearchParams(query: ProductQuery): URLSearchParams {
  const params = new URLSearchParams()
  if (query.page > 1) params.set('page', String(query.page))
  if (query.name.trim()) params.set('name', query.name.trim())
  if (query.minPrice.trim()) params.set('minPrice', query.minPrice.trim())
  if (query.maxPrice.trim()) params.set('maxPrice', query.maxPrice.trim())
  if (query.inStock) params.set('inStock', query.inStock)
  if (query.sortBy !== 'name') params.set('sortBy', query.sortBy)
  if (query.sortDir !== 'asc') params.set('sortDir', query.sortDir)
  return params
}

export function toApiQuery(query: ProductQuery, includeInactive = false): string {
  const params = new URLSearchParams()
  params.set('page', String(query.page))
  params.set('pageSize', String(query.pageSize))
  params.set('sortBy', query.sortBy)
  params.set('sortDir', query.sortDir)
  if (query.name.trim()) params.set('name', query.name.trim())
  if (query.minPrice.trim()) params.set('minPrice', query.minPrice.trim())
  if (query.maxPrice.trim()) params.set('maxPrice', query.maxPrice.trim())
  if (query.inStock) params.set('inStock', query.inStock)
  if (includeInactive) params.set('includeInactive', 'true')
  return params.toString()
}
