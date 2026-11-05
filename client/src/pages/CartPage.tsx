import { FormEvent, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { orderApi } from '../api/client'
import { useAuth } from '../auth/AuthContext'
import { useCart } from '../cart/CartContext'
import { Banner, Empty } from '../components/States'
import { cartFingerprint, checkoutKey, clearCheckoutKey } from '../lib/checkout'
import { formatMoney } from '../lib/format'
import { messageFor } from '../lib/problem'

const sessionStore = {
  getItem: (key: string) => sessionStorage.getItem(key),
  setItem: (key: string, value: string) => sessionStorage.setItem(key, value),
  removeItem: (key: string) => sessionStorage.removeItem(key),
}

export function CartPage() {
  const { lines, setQuantity, remove, clear } = useCart()
  const { user } = useAuth()
  const navigate = useNavigate()
  const [error, setError] = useState('')
  const [pending, setPending] = useState(false)
  const subtotal = lines.reduce((sum, line) => sum + line.price * line.quantity, 0)

  async function placeOrder(event: FormEvent) {
    event.preventDefault()
    if (!user) {
      navigate('/login', { state: { from: '/cart' } })
      return
    }
    if (lines.length === 0 || pending) return

    const key = checkoutKey(cartFingerprint(lines), sessionStore)
    setPending(true)
    setError('')
    try {
      const order = await orderApi.place(
        lines.map((line) => ({ productId: line.productId, quantity: line.quantity })),
        key,
      )
      clearCheckoutKey(sessionStore)
      clear()
      navigate('/orders', { state: { placedId: order.id } })
    } catch (caught) {
      setError(messageFor(caught))
    } finally {
      setPending(false)
    }
  }

  if (lines.length === 0) {
    return (
      <section>
        <p className="eyebrow">Cart</p>
        <Empty title="Your cart is empty" detail="Add something from the catalog. The cart stays in this browser if you refresh." />
        <Link className="button" to="/">
          Browse products
        </Link>
      </section>
    )
  }

  return (
    <section>
      <p className="eyebrow">Cart</p>
      <h1>Checkout</h1>
      {error ? <Banner tone="error">{error}</Banner> : null}
      <ul className="lines">
        {lines.map((line) => (
          <li key={line.productId}>
            <div>
              <strong>{line.name}</strong>
              <p className="muted">{line.sku}</p>
            </div>
            <label>
              Qty
              <input
                type="number"
                min={1}
                max={line.stockQuantity}
                value={line.quantity}
                onChange={(event) => setQuantity(line.productId, Number(event.target.value))}
              />
            </label>
            <span>{formatMoney(line.price * line.quantity)}</span>
            <button type="button" className="button ghost" onClick={() => remove(line.productId)}>
              Remove
            </button>
          </li>
        ))}
      </ul>
      <p className="total">Subtotal {formatMoney(subtotal)}</p>
      <p className="muted">
        Discounts are calculated on the server: 5% when the subtotal is above ₹10,000, plus an extra 10% on every 5th order. A double-click uses the same idempotency key, so it cannot create a second order.
      </p>
      <form onSubmit={placeOrder}>
        <button className="button" type="submit" disabled={pending}>
          {pending ? 'Placing order…' : user ? 'Place order' : 'Sign in to place order'}
        </button>
      </form>
    </section>
  )
}
