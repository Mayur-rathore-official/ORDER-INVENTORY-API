import type { CartLine } from '../types'

export interface KeyValueStore {
  getItem(key: string): string | null
  setItem(key: string, value: string): void
  removeItem(key: string): void
}

const storageKey = 'oi.checkout-key'

export function cartFingerprint(lines: Pick<CartLine, 'productId' | 'quantity'>[]): string {
  return [...lines]
    .map((line) => `${line.productId}:${line.quantity}`)
    .sort()
    .join('|')
}

export function checkoutKey(
  fingerprint: string,
  store: KeyValueStore,
  createId: () => string = () => crypto.randomUUID(),
): string {
  const raw = store.getItem(storageKey)
  if (raw) {
    try {
      const parsed = JSON.parse(raw) as { fingerprint?: string; key?: string }
      if (parsed.fingerprint === fingerprint && parsed.key && parsed.key.length >= 8 && parsed.key.length <= 128) {
        return parsed.key
      }
    } catch {
      // A damaged key is replaced below.
    }
  }

  const key = createId()
  store.setItem(storageKey, JSON.stringify({ fingerprint, key }))
  return key
}

export function clearCheckoutKey(store: KeyValueStore): void {
  store.removeItem(storageKey)
}

export function memoryStore(): KeyValueStore {
  const map = new Map<string, string>()
  return {
    getItem: (key) => map.get(key) ?? null,
    setItem: (key, value) => {
      map.set(key, value)
    },
    removeItem: (key) => {
      map.delete(key)
    },
  }
}

const cartKey = 'oi.cart'

export function loadCart(store: KeyValueStore): CartLine[] {
  const raw = store.getItem(cartKey)
  if (!raw) return []
  try {
    const parsed = JSON.parse(raw) as CartLine[]
    if (!Array.isArray(parsed)) return []
    return parsed.filter(
      (line) =>
        typeof line?.productId === 'string' &&
        typeof line.name === 'string' &&
        typeof line.quantity === 'number' &&
        line.quantity > 0,
    )
  } catch {
    return []
  }
}

export function saveCart(store: KeyValueStore, lines: CartLine[]): void {
  store.setItem(cartKey, JSON.stringify(lines))
}
