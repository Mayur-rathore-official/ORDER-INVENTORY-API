import { Navigate, Route, Routes } from 'react-router-dom'
import { GuestOnly, RequireAdmin, RequireAuth } from './components/Guards'
import { Layout } from './components/Layout'
import { AdminOrdersPage } from './pages/AdminOrdersPage'
import { AdminProductsPage } from './pages/AdminProductsPage'
import { CartPage } from './pages/CartPage'
import { CatalogPage } from './pages/CatalogPage'
import { LoginPage } from './pages/LoginPage'
import { OrdersPage } from './pages/OrdersPage'
import { RegisterPage } from './pages/RegisterPage'
import { ReportPage } from './pages/ReportPage'

export function App() {
  return (
    <Routes>
      <Route element={<Layout />}>
        <Route index element={<CatalogPage />} />
        <Route path="cart" element={<CartPage />} />
        <Route element={<GuestOnly />}>
          <Route path="login" element={<LoginPage />} />
          <Route path="register" element={<RegisterPage />} />
        </Route>
        <Route element={<RequireAuth />}>
          <Route path="orders" element={<OrdersPage />} />
        </Route>
        <Route element={<RequireAdmin />}>
          <Route path="admin/products" element={<AdminProductsPage />} />
          <Route path="admin/orders" element={<AdminOrdersPage />} />
          <Route path="admin/report" element={<ReportPage />} />
        </Route>
        <Route path="*" element={<Navigate to="/" replace />} />
      </Route>
    </Routes>
  )
}
