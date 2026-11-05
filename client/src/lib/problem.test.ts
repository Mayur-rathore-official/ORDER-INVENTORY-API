import { describe, expect, it } from 'vitest'
import { ApiError, messageFor } from './problem'
import { parseProductQuery, toSearchParams } from './catalogQuery'

describe('user-facing API messages', () => {
  it('explains stock conflicts and login rate limits', () => {
    const stock = new ApiError(409, 'Insufficient stock', "Insufficient stock for product 'SKU-1013'. Requested 1, available 0.")
    const limited = new ApiError(429, 'Too many login attempts', 'Maximum 5 login attempts per minute from this IP.')

    expect(messageFor(stock)).toContain('SKU-1013')
    expect(messageFor(limited)).toBe('Too many login attempts. Wait a minute and try again.')
  })
})

describe('catalog URL state', () => {
  it('keeps filters in the query string and reads them back', () => {
    const params = toSearchParams({
      page: 2,
      pageSize: 8,
      name: 'monitor',
      minPrice: '100',
      maxPrice: '20000',
      inStock: 'true',
      sortBy: 'price',
      sortDir: 'desc',
    })

    expect(params.toString()).toContain('name=monitor')
    expect(params.get('inStock')).toBe('true')

    const parsed = parseProductQuery(params)
    expect(parsed.page).toBe(2)
    expect(parsed.sortBy).toBe('price')
    expect(parsed.sortDir).toBe('desc')
    expect(parsed.minPrice).toBe('100')
  })
})
