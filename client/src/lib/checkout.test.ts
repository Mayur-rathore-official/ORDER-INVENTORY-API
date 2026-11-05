import { describe, expect, it } from 'vitest'
import { cartFingerprint, checkoutKey, clearCheckoutKey, memoryStore } from './checkout'

describe('checkout idempotency key', () => {
  it('reuses the same key when the cart has not changed', () => {
    const store = memoryStore()
    const fingerprint = cartFingerprint([
      { productId: 'b', quantity: 1 },
      { productId: 'a', quantity: 2 },
    ])
    let calls = 0
    const createId = () => {
      calls += 1
      return 'idempotency-key-0001'
    }

    const first = checkoutKey(fingerprint, store, createId)
    const second = checkoutKey(fingerprint, store, createId)

    expect(first).toBe('idempotency-key-0001')
    expect(second).toBe(first)
    expect(calls).toBe(1)
  })

  it('issues a new key after the cart changes and after a successful checkout', () => {
    const store = memoryStore()
    const keys = ['key-one-aaaa', 'key-two-bbbb']
    const createId = () => keys.shift() ?? 'key-three-ccc'
    const firstCart = cartFingerprint([{ productId: 'a', quantity: 1 }])
    const secondCart = cartFingerprint([{ productId: 'a', quantity: 2 }])

    expect(checkoutKey(firstCart, store, createId)).toBe('key-one-aaaa')
    expect(checkoutKey(secondCart, store, createId)).toBe('key-two-bbbb')

    clearCheckoutKey(store)
    expect(checkoutKey(secondCart, store, createId)).toBe('key-three-ccc')
  })
})
