import { Navigate, Outlet, useLocation } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { Loading } from './States'

export function RequireAuth() {
  const { user, ready } = useAuth()
  const location = useLocation()
  if (!ready) return <Loading label="Checking your session…" />
  if (!user) return <Navigate to="/login" replace state={{ from: location.pathname }} />
  return <Outlet />
}

export function RequireAdmin() {
  const { user, ready } = useAuth()
  if (!ready) return <Loading label="Checking your session…" />
  if (!user) return <Navigate to="/login" replace state={{ from: '/admin/products' }} />
  if (user.role !== 'Admin') {
    return (
      <section className="panel">
        <h1>Admins only</h1>
        <p>This area is limited to store administrators.</p>
      </section>
    )
  }
  return <Outlet />
}

export function GuestOnly() {
  const { user, ready } = useAuth()
  if (!ready) return <Loading />
  if (user) return <Navigate to={user.role === 'Admin' ? '/admin/orders' : '/'} replace />
  return <Outlet />
}
