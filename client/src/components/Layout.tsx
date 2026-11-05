import { NavLink, Outlet } from 'react-router-dom'
import { useState } from 'react'
import { useAuth } from '../auth/AuthContext'
import { useCart } from '../cart/CartContext'

export function Layout() {
  const { user, logout } = useAuth()
  const { count } = useCart()
  const [open, setOpen] = useState(false)

  return (
    <div className="shell">
      <header className="topbar">
        <NavLink className="brand" to="/" onClick={() => setOpen(false)}>
          Northline
        </NavLink>
        <button type="button" className="menu" aria-expanded={open} onClick={() => setOpen((value) => !value)}>
          Menu
        </button>
        <nav className={open ? 'nav open' : 'nav'} onClick={() => setOpen(false)}>
          <NavLink to="/">Catalog</NavLink>
          <NavLink to="/cart">Cart{count > 0 ? ` (${count})` : ''}</NavLink>
          {user ? <NavLink to="/orders">My orders</NavLink> : null}
          {user?.role === 'Admin' ? (
            <>
              <NavLink to="/admin/products">Products</NavLink>
              <NavLink to="/admin/orders">All orders</NavLink>
              <NavLink to="/admin/report">Report</NavLink>
            </>
          ) : null}
        </nav>
        <div className="account">
          {user ? (
            <>
              <span>{user.fullName}</span>
              <button type="button" className="button ghost" onClick={logout}>
                Sign out
              </button>
            </>
          ) : (
            <NavLink className="button" to="/login">
              Sign in
            </NavLink>
          )}
        </div>
      </header>
      <main>
        <Outlet />
      </main>
    </div>
  )
}
