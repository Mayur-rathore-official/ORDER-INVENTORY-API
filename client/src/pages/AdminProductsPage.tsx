import { FormEvent, useEffect, useState } from 'react'
import { productApi } from '../api/client'
import { Banner, Empty, ErrorState, Loading } from '../components/States'
import { formatMoney } from '../lib/format'
import { messageFor } from '../lib/problem'
import type { Product } from '../types'

const emptyForm = {
  id: '',
  sku: '',
  name: '',
  price: '',
  stockQuantity: '',
  isActive: true,
  rowVersion: '',
}

export function AdminProductsPage() {
  const [products, setProducts] = useState<Product[] | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [formError, setFormError] = useState('')
  const [notice, setNotice] = useState('')
  const [pending, setPending] = useState(false)
  const [form, setForm] = useState(emptyForm)

  function load() {
    setLoading(true)
    setError('')
    productApi
      .list('page=1&pageSize=100&sortBy=sku&sortDir=asc&includeInactive=true')
      .then((page) => setProducts(page.items))
      .catch((caught: unknown) => setError(messageFor(caught)))
      .finally(() => setLoading(false))
  }

  useEffect(() => {
    load()
  }, [])

  function edit(product: Product) {
    setForm({
      id: product.id,
      sku: product.sku,
      name: product.name,
      price: String(product.price),
      stockQuantity: String(product.stockQuantity),
      isActive: product.isActive,
      rowVersion: product.rowVersion,
    })
    setFormError('')
    setNotice('')
  }

  async function onSubmit(event: FormEvent) {
    event.preventDefault()
    setPending(true)
    setFormError('')
    setNotice('')
    const body = {
      sku: form.sku.trim(),
      name: form.name.trim(),
      price: Number(form.price),
      stockQuantity: Number(form.stockQuantity),
      isActive: form.isActive,
    }
    try {
      if (form.id) {
        await productApi.update(form.id, { ...body, rowVersion: form.rowVersion })
        setNotice('Product updated. The catalog cache was cleared.')
      } else {
        await productApi.create(body)
        setNotice('Product created.')
      }
      setForm(emptyForm)
      load()
    } catch (caught) {
      setFormError(messageFor(caught))
    } finally {
      setPending(false)
    }
  }

  return (
    <section className="split">
      <div>
        <p className="eyebrow">Admin</p>
        <h1>{form.id ? 'Edit product' : 'New product'}</h1>
        {formError ? <Banner tone="error">{formError}</Banner> : null}
        {notice ? <Banner tone="ok">{notice}</Banner> : null}
        <form className="stack" onSubmit={onSubmit}>
          <label>
            SKU
            <input value={form.sku} onChange={(event) => setForm({ ...form, sku: event.target.value })} required />
          </label>
          <label>
            Name
            <input value={form.name} onChange={(event) => setForm({ ...form, name: event.target.value })} required />
          </label>
          <label>
            Price
            <input value={form.price} onChange={(event) => setForm({ ...form, price: event.target.value })} inputMode="decimal" required />
          </label>
          <label>
            Stock
            <input value={form.stockQuantity} onChange={(event) => setForm({ ...form, stockQuantity: event.target.value })} inputMode="numeric" required />
          </label>
          <label className="check">
            <input type="checkbox" checked={form.isActive} onChange={(event) => setForm({ ...form, isActive: event.target.checked })} />
            Active
          </label>
          <div className="row">
            <button className="button" type="submit" disabled={pending}>
              {pending ? 'Saving…' : form.id ? 'Save changes' : 'Create product'}
            </button>
            {form.id ? (
              <button type="button" className="button ghost" onClick={() => setForm(emptyForm)}>
                Cancel edit
              </button>
            ) : null}
          </div>
        </form>
      </div>
      <div>
        <h2>Catalog</h2>
        {loading ? <Loading label="Loading products…" /> : null}
        {!loading && error ? <ErrorState message={error} onRetry={load} /> : null}
        {!loading && !error && products && products.length === 0 ? <Empty title="No products" detail="Create the first product with the form." /> : null}
        {!loading && !error && products && products.length > 0 ? (
          <ul className="admin-list">
            {products.map((product) => (
              <li key={product.id}>
                <div>
                  <strong>{product.name}</strong>
                  <p className="muted">
                    {product.sku} · {formatMoney(product.price)} · {product.stockQuantity} in stock
                    {product.isActive ? '' : ' · inactive'}
                  </p>
                </div>
                <button type="button" className="button ghost" onClick={() => edit(product)}>
                  Edit
                </button>
              </li>
            ))}
          </ul>
        ) : null}
      </div>
    </section>
  )
}
