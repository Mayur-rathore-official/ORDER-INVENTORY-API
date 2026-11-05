import { useEffect, useState } from 'react'
import { useLocation } from 'react-router-dom'
import { orderApi } from '../api/client'
import { Banner, Empty, ErrorState, Loading } from '../components/States'
import { formatDate, formatMoney } from '../lib/format'
import { messageFor } from '../lib/problem'
import type { Order, PagedResult } from '../types'

export function OrdersPage() {
  const location = useLocation()
  const placedId = (location.state as { placedId?: string } | null)?.placedId
  const [page, setPage] = useState(1)
  const [data, setData] = useState<PagedResult<Order> | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [actionError, setActionError] = useState('')
  const [busyId, setBusyId] = useState<string | null>(null)

  function load(nextPage = page) {
    setLoading(true)
    setError('')
    orderApi
      .mine(nextPage)
      .then(setData)
      .catch((caught: unknown) => setError(messageFor(caught)))
      .finally(() => setLoading(false))
  }

  useEffect(() => {
    let cancelled = false
    setLoading(true)
    setError('')
    orderApi
      .mine(page)
      .then((result) => {
        if (!cancelled) setData(result)
      })
      .catch((caught: unknown) => {
        if (!cancelled) setError(messageFor(caught))
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })
    return () => {
      cancelled = true
    }
  }, [page])

  async function cancel(order: Order) {
    setBusyId(order.id)
    setActionError('')
    try {
      await orderApi.changeStatus(order.id, 'Cancelled')
      load(page)
    } catch (caught) {
      setActionError(messageFor(caught))
    } finally {
      setBusyId(null)
    }
  }

  const totalPages = data ? Math.max(1, Math.ceil(data.totalCount / data.pageSize)) : 1

  return (
    <section>
      <p className="eyebrow">Orders</p>
      <h1>My orders</h1>
      {placedId ? <Banner tone="ok">Order placed. Stock was reserved for every line in one transaction.</Banner> : null}
      {actionError ? <Banner tone="error">{actionError}</Banner> : null}
      {loading ? <Loading label="Loading your orders…" /> : null}
      {!loading && error ? <ErrorState message={error} onRetry={() => load(page)} /> : null}
      {!loading && !error && data && data.items.length === 0 ? (
        <Empty title="No orders yet" detail="When you check out, the order and its status show up here." />
      ) : null}
      {!loading && !error && data && data.items.length > 0 ? (
        <>
          <ul className="orders">
            {data.items.map((order) => (
              <li key={order.id} className="panel">
                <div className="order-head">
                  <div>
                    <span className={`badge status-${order.status.toLowerCase()}`}>{order.status}</span>
                    <p className="muted">{formatDate(order.createdAt)}</p>
                  </div>
                  <strong>{formatMoney(order.total)}</strong>
                </div>
                <p className="muted">
                  Subtotal {formatMoney(order.subTotal)} · Discount {formatMoney(order.discount)}
                </p>
                <ul>
                  {order.items.map((item) => (
                    <li key={item.id}>
                      {item.quantity} × {item.productName} ({item.sku}) at {formatMoney(item.unitPrice)}
                    </li>
                  ))}
                </ul>
                {order.status === 'Pending' ? (
                  <button type="button" className="button ghost" disabled={busyId === order.id} onClick={() => cancel(order)}>
                    {busyId === order.id ? 'Cancelling…' : 'Cancel order'}
                  </button>
                ) : null}
              </li>
            ))}
          </ul>
          <div className="pager">
            <button type="button" className="button ghost" disabled={page <= 1} onClick={() => setPage((value) => value - 1)}>
              Previous
            </button>
            <span>
              Page {data.page} of {totalPages}
            </span>
            <button type="button" className="button ghost" disabled={page >= totalPages} onClick={() => setPage((value) => value + 1)}>
              Next
            </button>
          </div>
        </>
      ) : null}
    </section>
  )
}
