/*
    SQL Server queries for the Order Inventory schema.
    Revenue and spend count Confirmed and Shipped orders only.
    Pending and Cancelled orders are not revenue.
*/

/* 1. Top 5 customers by total spend in the last 30 days. */
SELECT TOP (5)
    u.Id,
    u.FullName,
    u.Email,
    SUM(o.Total) AS TotalSpend,
    COUNT_BIG(*) AS OrderCount
FROM dbo.Orders AS o
INNER JOIN dbo.Users AS u ON u.Id = o.CustomerId
WHERE o.Status IN (N'Confirmed', N'Shipped')
  AND o.CreatedAt >= DATEADD(DAY, -30, SYSUTCDATETIME())
GROUP BY u.Id, u.FullName, u.Email
ORDER BY TotalSpend DESC, u.FullName ASC;

/* 2. Products that have never been ordered. */
SELECT
    p.Id,
    p.Sku,
    p.Name,
    p.Price,
    p.StockQuantity,
    p.IsActive
FROM dbo.Products AS p
WHERE NOT EXISTS (
    SELECT 1
    FROM dbo.OrderItems AS oi
    WHERE oi.ProductId = p.Id
)
ORDER BY p.Sku;

/* 3. Monthly revenue for the current year, with a running total. */
SELECT
    MONTH(o.CreatedAt) AS [Month],
    SUM(o.Total) AS Revenue,
    SUM(SUM(o.Total)) OVER (
        ORDER BY MONTH(o.CreatedAt)
        ROWS UNBOUNDED PRECEDING
    ) AS RunningTotal
FROM dbo.Orders AS o
WHERE o.Status IN (N'Confirmed', N'Shipped')
  AND YEAR(o.CreatedAt) = YEAR(SYSUTCDATETIME())
GROUP BY MONTH(o.CreatedAt)
ORDER BY [Month];

/* 4. Each customer's second-highest order total.
      Customers with fewer than two orders do not appear. */
SELECT
    ranked.CustomerId,
    ranked.FullName,
    ranked.Email,
    ranked.Total AS SecondHighestTotal
FROM (
    SELECT
        u.Id AS CustomerId,
        u.FullName,
        u.Email,
        o.Total,
        ROW_NUMBER() OVER (
            PARTITION BY o.CustomerId
            ORDER BY o.Total DESC, o.CreatedAt DESC, o.Id
        ) AS RowNumber
    FROM dbo.Orders AS o
    INNER JOIN dbo.Users AS u ON u.Id = o.CustomerId
) AS ranked
WHERE ranked.RowNumber = 2;

/* 5. Products whose stock is below that product's average quantity per order.
      AVG of an int is an int in SQL Server, so Quantity is cast before the average. */
SELECT
    p.Id,
    p.Sku,
    p.Name,
    p.StockQuantity,
    sold.AverageQuantity
FROM dbo.Products AS p
INNER JOIN (
    SELECT
        oi.ProductId,
        AVG(CAST(oi.Quantity AS decimal(18, 4))) AS AverageQuantity
    FROM dbo.OrderItems AS oi
    GROUP BY oi.ProductId
) AS sold ON sold.ProductId = p.Id
WHERE p.StockQuantity < sold.AverageQuantity
ORDER BY p.Sku;
