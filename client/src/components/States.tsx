import type { ReactNode } from 'react'

export function Loading({ label = 'Loading…' }: { label?: string }) {
  return (
    <div className="state" role="status">
      <span className="spinner" aria-hidden="true" />
      <p>{label}</p>
    </div>
  )
}

export function Empty({ title, detail }: { title: string; detail: string }) {
  return (
    <div className="state">
      <h2>{title}</h2>
      <p>{detail}</p>
    </div>
  )
}

export function ErrorState({ message, onRetry }: { message: string; onRetry?: () => void }) {
  return (
    <div className="state error" role="alert">
      <h2>Something went wrong</h2>
      <p>{message}</p>
      {onRetry ? (
        <button type="button" className="button" onClick={onRetry}>
          Try again
        </button>
      ) : null}
    </div>
  )
}

export function Banner({ tone, children }: { tone: 'error' | 'info' | 'ok'; children: ReactNode }) {
  return <div className={`banner banner-${tone}`}>{children}</div>
}
