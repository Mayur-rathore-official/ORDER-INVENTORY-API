import { FormEvent, useEffect, useRef, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { productApi } from '../api/client'
import { useCart } from '../cart/CartContext'
import { Banner, Empty, ErrorState, Loading } from '../components/States'
import { parseProductQuery, toApiQuery, toSearchParams } from '../lib/catalogQuery'
import { formatMoney } from '../lib/format'
import { messageFor } from '../lib/problem'
import type { PagedResult, Product, ProductQuery } from '../types'

export function CatalogPage() {
  const [params, setParams] = useSearchParams()
  const query = parseProductQuery(params)
  const queryRef = useRef(query)
  queryRef.current = query
  const [draftName, setDraftName] = useState(query.name)
  const [pageData, setPageData] = useState<PagedResult<Product> | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const { add } = useCart()

  useEffect(() => {
    setDraftName(query.name)
  }, [query.name])

  useEffect(() => {
    const handle = window.setTimeout(() => {
      const current = queryRef.current
      if (draftName === current.name) return
      setParams(toSearchParams({ ...current, name: draftName, page: 1 }), { replace: true })
    }, 300)
    return () => window.clearTimeout(handle)
  }, [draftName, setParams])

  useEffect(() => {
    let cancelled = false
    setLoading(true)
    setError('')
    productApi
      .list(toApiQuery(query))
      .then((result) => {
        if (!cancelled) setPageData(result)
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
  }, [params])

  function update(patch: Partial<ProductQuery>, resetPage = true) {
    const next = toSearchParams({ ...query, ...patch, page: resetPage ? 1 : (patch.page ?? query.page) })
    setParams(next)
  }

  function onFilter(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const data = new FormData(event.currentTarget)
    update({
      minPrice: String(data.get('minPrice') ?? ''),
      maxPrice: String(data.get('maxPrice') ?? ''),
      inStock: String(data.get('inStock') ?? '') as ProductQuery['inStock'],
      sortBy: String(data.get('sortBy') ?? 'name') as ProductQuery['sortBy'],
      sortDir: String(data.get('sortDir') ?? 'asc') as ProductQuery['sortDir'],
    })
  }

  const totalPages = pageData ? Math.max(1, Math.ceil(pageData.totalCount / pageData.pageSize)) : 1

  return (
    <section>
      <div className="page-head">
        <div>
          <p className="eyebrow">Catalog</p>
          <h1>Goods in stock</h1>
        </div>
      </div>
      <form className="filters" key={params.toString()} onSubmit={onFilter}>
        <label>
          Search
          <input value={draftName} onChange={(event) => setDraftName(event.target.value)} placeholder="Name" />
        </label>
        <label>
          Min price
          <input name="minPrice" defaultValue={query.minPrice} inputMode="decimal" />
        </label>
        <label>
          Max price
          <input name="maxPrice" defaultValue={query.maxPrice} inputMode="decimal" />
        </label>
        <label>
          Stock
          <select name="inStock" defaultValue={query.inStock}>
            <option value="">All</option>
            <option value="true">In stock</option>
            <option value="false">Out of stock</option>
          </select>
        </label>
        <label>
          Sort
          <select name="sortBy" defaultValue={query.sortBy}>
            <option value="name">Name</option>
            <option value="price">Price</option>
            <option value="sku">SKU</option>
          </select>
        </label>
        <label>
          Direction
          <select name="sortDir" defaultValue={query.sortDir}>
            <option value="asc">Ascending</option>
            <option value="desc">Descending</option>
          </select>
        </label>
        <button className="button" type="submit">
          Apply
        </button>
      </form>
      {notice ? <Banner tone="ok">{notice}</Banner> : null}
      {loading ? <Loading label="Loading products…" /> : null}
      {!loading && error ? <ErrorState message={error} onRetry={() => setParams(params)} /> : null}
      {!loading && !error && pageData && pageData.items.length === 0 ? (
        <Empty title="No products match" detail="Try a wider price range or clear the search." />
      ) : null}
      {!loading && !error && pageData && pageData.items.length > 0 ? (
        <>
          <ul className="catalog">
            {pageData.items.map((product) => (
              <li key={product.id} className="card">
                <p className="sku">{product.sku}</p>
                <h2>{product.name}</h2>
                <p className="price">{formatMoney(product.price)}</p>
                <p className={product.stockQuantity > 0 ? 'stock' : 'stock out'}>
                  {product.stockQuantity > 0 ? `${product.stockQuantity} in stock` : 'Out of stock'}
                </p>
                <button
                  type="button"
                  className="button"
                  disabled={product.stockQuantity < 1}
                  onClick={() => {
                    add(product)
                    setNotice(`${product.name} added to the cart.`)
                  }}
                >
                  Add to cart
                </button>
              </li>
            ))}
          </ul>
          <div className="pager">
            <button type="button" className="button ghost" disabled={query.page <= 1} onClick={() => update({ page: query.page - 1 }, false)}>
              Previous
            </button>
            <span>
              Page {pageData.page} of {totalPages} · {pageData.totalCount} products
            </span>
            <button
              type="button"
              className="button ghost"
              disabled={query.page >= totalPages}
              onClick={() => update({ page: query.page + 1 }, false)}
            >
              Next
            </button>
          </div>
        </>
      ) : null}
    </section>
  )
}
