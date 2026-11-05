import { createContext, useContext, useMemo, useState, type ReactNode } from 'react'
import { loadCart, saveCart } from '../lib/checkout'
import type { CartLine, Product } from '../types'

interface CartState {
  lines: CartLine[]
  count: number
  add: (product: Product) => void
  setQuantity: (productId: string, quantity: number) => void
  remove: (productId: string) => void
  clear: () => void
}

const CartContext = createContext<CartState | null>(null)

const browserStore = {
  getItem: (key: string) => localStorage.getItem(key),
  setItem: (key: string, value: string) => localStorage.setItem(key, value),
  removeItem: (key: string) => localStorage.removeItem(key),
}

export function CartProvider({ children }: { children: ReactNode }) {
  const [lines, setLines] = useState<CartLine[]>(() => loadCart(browserStore))

  const value = useMemo<CartState>(() => {
    const commit = (next: CartLine[]) => {
      saveCart(browserStore, next)
      setLines(next)
    }
    return {
      lines,
      count: lines.reduce((sum, line) => sum + line.quantity, 0),
      add: (product) => {
        if (!product.isActive || product.stockQuantity < 1) return
        const existing = lines.find((line) => line.productId === product.id)
        if (existing) {
          commit(
            lines.map((line) =>
              line.productId === product.id
                ? { ...line, quantity: Math.min(line.quantity + 1, product.stockQuantity), stockQuantity: product.stockQuantity, price: product.price }
                : line,
            ),
          )
          return
        }
        commit([
          ...lines,
          {
            productId: product.id,
            sku: product.sku,
            name: product.name,
            price: product.price,
            quantity: 1,
            stockQuantity: product.stockQuantity,
          },
        ])
      },
      setQuantity: (productId, quantity) => {
        commit(
          lines
            .map((line) => (line.productId === productId ? { ...line, quantity: Math.max(1, Math.min(quantity, line.stockQuantity || quantity)) } : line))
            .filter((line) => line.quantity > 0),
        )
      },
      remove: (productId) => commit(lines.filter((line) => line.productId !== productId)),
      clear: () => commit([]),
    }
  }, [lines])

  return <CartContext.Provider value={value}>{children}</CartContext.Provider>
}

export function useCart(): CartState {
  const value = useContext(CartContext)
  if (!value) throw new Error('useCart must be used inside CartProvider')
  return value
}
