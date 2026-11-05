import { FormEvent, useEffect, useState } from 'react'
import { reportApi } from '../api/client'
import { Empty, ErrorState, Loading } from '../components/States'
import { formatMoney } from '../lib/format'
import { messageFor } from '../lib/problem'
import type { TopCustomer } from '../types'

export function ReportPage() {
  const [days, setDays] = useState(30)
  const [draft, setDraft] = useState('30')
  const [rows, setRows] = useState<TopCustomer[] | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')

  function load(nextDays = days) {
    setLoading(true)
    setError('')
    reportApi
      .topCustomers(nextDays)
      .then(setRows)
      .catch((caught: unknown) => setError(messageFor(caught)))
      .finally(() => setLoading(false))
  }

  useEffect(() => {
    let cancelled = false
    setLoading(true)
    setError('')
    reportApi
      .topCustomers(days)
      .then((result) => {
        if (!cancelled) setRows(result)
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
  }, [days])

  function onSubmit(event: FormEvent) {
    event.preventDefault()
    const value = Number(draft)
    if (!Number.isInteger(value) || value < 1 || value > 366) {
      setError('Days must be a whole number from 1 to 366.')
      return
    }
    setDays(value)
  }

  const max = rows?.reduce((highest, row) => Math.max(highest, row.totalSpend), 0) ?? 0

  return (
    <section>
      <p className="eyebrow">Admin</p>
      <h1>Top customers</h1>
      <p className="muted">Confirmed and shipped spend, calculated in one SQL query.</p>
      <form className="inline" onSubmit={onSubmit}>
        <label>
          Days
          <input value={draft} onChange={(event) => setDraft(event.target.value)} inputMode="numeric" />
        </label>
        <button className="button" type="submit">
          Update
        </button>
      </form>
      {loading ? <Loading label="Loading the report…" /> : null}
      {!loading && error ? <ErrorState message={error} onRetry={() => load(days)} /> : null}
      {!loading && !error && rows && rows.length === 0 ? (
        <Empty title="No qualifying orders" detail="Confirmed or shipped orders in this window will appear here." />
      ) : null}
      {!loading && !error && rows && rows.length > 0 ? (
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>Customer</th>
                <th>Orders</th>
                <th>Spend</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((row) => (
                <tr key={row.customerId}>
                  <td>
                    <strong>{row.fullName}</strong>
                    <div className="muted">{row.email}</div>
                    <div className="bar" style={{ width: `${max === 0 ? 0 : (row.totalSpend / max) * 100}%` }} />
                  </td>
                  <td>{row.orderCount}</td>
                  <td>{formatMoney(row.totalSpend)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : null}
    </section>
  )
}
