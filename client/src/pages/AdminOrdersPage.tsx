import { useEffect, useState } from 'react'
import { orderApi } from '../api/client'
import { Banner, Empty, ErrorState, Loading } from '../components/States'
import { formatDate, formatMoney } from '../lib/format'
import { messageFor } from '../lib/problem'
import type { Order, PagedResult } from '../types'

const nextStatus: Record<Order['status'], Order['status'][]> = {
  Pending: ['Confirmed', 'Cancelled'],
  Confirmed: ['Shipped', 'Cancelled'],
  Shipped: [],
  Cancelled: [],
}

export function AdminOrdersPage() {
  const [status, setStatus] = useState('')
  const [page, setPage] = useState(1)
  const [data, setData] = useState<PagedResult<Order> | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [actionError, setActionError] = useState('')
  const [busyId, setBusyId] = useState<string | null>(null)

  function load(nextPage = page, nextStatus = status) {
    setLoading(true)
    setError('')
    orderApi
      .list(nextPage, nextStatus || undefined)
      .then(setData)
      .catch((caught: unknown) => setError(messageFor(caught)))
      .finally(() => setLoading(false))
  }

  useEffect(() => {
    let cancelled = false
    setLoading(true)
    setError('')
    orderApi
      .list(page, status || undefined)
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
  }, [page, status])

  async function change(order: Order, next: string) {
    setBusyId(order.id)
    setActionError('')
    try {
      await orderApi.changeStatus(order.id, next)
      load(page, status)
    } catch (caught) {
      setActionError(messageFor(caught))
    } finally {
      setBusyId(null)
    }
  }

  const totalPages = data ? Math.max(1, Math.ceil(data.totalCount / data.pageSize)) : 1

  return (
    <section>
      <p className="eyebrow">Admin</p>
      <h1>Orders</h1>
      <label className="inline">
        Status
        <select
          value={status}
          onChange={(event) => {
            setPage(1)
            setStatus(event.target.value)
          }}
        >
          <option value="">All</option>
          <option value="Pending">Pending</option>
          <option value="Confirmed">Confirmed</option>
          <option value="Shipped">Shipped</option>
          <option value="Cancelled">Cancelled</option>
        </select>
      </label>
      {actionError ? <Banner tone="error">{actionError}</Banner> : null}
      {loading ? <Loading label="Loading orders…" /> : null}
      {!loading && error ? <ErrorState message={error} onRetry={() => load(page, status)} /> : null}
      {!loading && !error && data && data.items.length === 0 ? <Empty title="No orders" detail="Nothing matches this status filter." /> : null}
      {!loading && !error && data && data.items.length > 0 ? (
        <>
          <ul className="orders">
            {data.items.map((order) => (
              <li key={order.id} className="panel">
                <div className="order-head">
                  <div>
                    <span className={`badge status-${order.status.toLowerCase()}`}>{order.status}</span>
                    <p>
                      {order.customerName} · {formatDate(order.createdAt)}
                    </p>
                  </div>
                  <strong>{formatMoney(order.total)}</strong>
                </div>
                <ul>
                  {order.items.map((item) => (
                    <li key={item.id}>
                      {item.quantity} × {item.productName}
                    </li>
                  ))}
                </ul>
                {nextStatus[order.status].length > 0 ? (
                  <div className="row">
                    {nextStatus[order.status].map((next) => (
                      <button key={next} type="button" className="button ghost" disabled={busyId === order.id} onClick={() => change(order, next)}>
                        {busyId === order.id ? 'Updating…' : `Mark ${next}`}
                      </button>
                    ))}
                  </div>
                ) : (
                  <p className="muted">This status is final.</p>
                )}
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
