using System.Linq;
using System.Text.Json;
using ABCRetailsFunctions.Entities;
using ABCRetailsFunctions.Helpers;
using ABCRetailsFunctions.Models;
using Azure.Data.Tables;
using Azure.Storage.Queues;
using Grpc.Core;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;


namespace ABCRetailsFunctions.Functions
{
    public class OrdersFunctions
    {
        private readonly string _conn;
        private readonly string _ordersTable;
        private readonly string _productsTable;
        private readonly string _customersTable;
        private readonly string _queueOrder;
        

        public OrdersFunctions(IConfiguration cfg)
        {
            // FIX: Read connection string from configuration
            _conn = cfg.GetConnectionString("STORAGE_CONNECTION") ?? throw new ArgumentNullException("STORAGE_CONNECTION");
            _ordersTable = cfg["TABLE_ORDER"] ?? "Orders";
            _productsTable = cfg["TABLE_PRODUCT"] ?? "Products";
            _customersTable = cfg["TABLE_CUSTOMER"] ?? "Customers";
            _queueOrder = cfg["QUEUE_ORDER_NOTIFICATIONS"] ?? "order-notifications";
        }

        [Function("Orders_List")]
        public async Task<HttpResponseData> List(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "orders")] HttpRequestData req)
        {
            var table = new TableClient(_conn, _ordersTable);
            await table.CreateIfNotExistsAsync();

            var items = new List<OrderDto>();
            await foreach (var e in table.QueryAsync<OrderEntity>(x => x.PartitionKey == "Order"))
            {
                items.Add(Map.ToDto(e));
            }

            var ordered = items.OrderByDescending(o => o.OrderDateUtc).ToList();
            return HttpJson.Ok(req, ordered);
        }

        [Function("Orders_Get")]
        public async Task<HttpResponseData> Get(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "orders/{id}")] HttpRequestData req, string id)
        {
            var table = new TableClient(_conn, _ordersTable);
            try
            {
                var e = await table.GetEntityAsync<OrderEntity>("Order", id);
                return HttpJson.Ok(req, Map.ToDto(e.Value));
            }
            catch
            {
                return HttpJson.NotFound(req, "Order not found");
            }
        }

        public record OrderCreate(string CustomerId, string ProductId, int Quantity);

        [Function("Orders_Create")]
        public async Task<HttpResponseData> Create(
    [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "orders")]
    HttpRequestData req)
        {
            var input = await HttpJson.ReadAsync<OrderCreate>(req);
            if (input is null || string.IsNullOrWhiteSpace(input.CustomerId) ||
                string.IsNullOrWhiteSpace(input.ProductId) || input.Quantity < 1)
                return HttpJson.Bad(req, "CustomerId, ProductId, and Quantity (>= 1) are required");

            var products = new TableClient(_conn, _productsTable);
            var customers = new TableClient(_conn, _customersTable);
            var orders = new TableClient(_conn, _ordersTable);

            ProductEntity product;
            CustomerEntity customer;

            // 1. Get Product
            try
            {
                product = (await products.GetEntityAsync<ProductEntity>("Product", input.ProductId)).Value;
            }
            catch { return HttpJson.Bad(req, "Invalid ProductId"); }

            // 2. Get Customer - This is critical for customer order visibility
            try
            {
                customer = (await customers.GetEntityAsync<CustomerEntity>("Customer", input.CustomerId)).Value;
            }
            catch { return HttpJson.Bad(req, "Invalid CustomerId"); }

            // 3. Check Stock
            if (product.StockAvailable < input.Quantity)
                return HttpJson.Bad(req, $"Insufficient stock. Available: {product.StockAvailable}");

            // 4. Create and save the order entity - CRITICAL: Include customer username
            var order = new OrderEntity
            {
                CustomerId = input.CustomerId,
                Username = customer.Username, // This ensures customer can see their orders
                ProductId = input.ProductId,
                ProductName = product.ProductName,
                ProductImageUrl = product.ImageUrl,
                Quantity = input.Quantity,
                UnitPrice = product.Price,
                TotalPrice = product.Price * input.Quantity,
                OrderDateUtc = DateTimeOffset.UtcNow,
                Status = "Submitted"
            };

            await orders.AddEntityAsync(order);

            // 5. Update product stock
            product.StockAvailable -= input.Quantity;
            await products.UpdateEntityAsync(product, product.ETag, TableUpdateMode.Replace);

            return HttpJson.Created(req, Map.ToDto(order));
        }



        public record OrderStatusUpdate(string Status);
        [Function("Orders_UpdateStatus")]
        public async Task<HttpResponseData> UpdateStatus(
    [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "orders/{id}/status")]
    HttpRequestData req, string id)
        {
            var input = await HttpJson.ReadAsync<OrderStatusUpdate>(req);
            if (input is null || string.IsNullOrWhiteSpace(input.Status))
                return HttpJson.Bad(req, "Status is required");

            var orders = new TableClient(_conn, _ordersTable);
            try
            {
                var resp = await orders.GetEntityAsync<OrderEntity>("Order", id);
                var e = resp.Value;
                e.Status = input.Status;
                await orders.UpdateEntityAsync(e, e.ETag, TableUpdateMode.Replace);
                return HttpJson.Ok(req, Map.ToDto(e));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating order status: {ex.Message}");
                return HttpJson.NotFound(req, "Order not found");
            }
        }

        [Function("Orders_Delete")]
        public async Task<HttpResponseData> Delete(
            [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "orders/{id}")] HttpRequestData req, string id)
        {
            var table = new TableClient(_conn, _ordersTable);
            await table.DeleteEntityAsync("Order", id);
            return HttpJson.NoContent(req);
        }

        [Function("Orders_Update")]
        public async Task<HttpResponseData> Update(
     [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "orders/{id}")] HttpRequestData req, string id)
        {
            var input = await HttpJson.ReadAsync<Dictionary<string, object>>(req);
            if (input is null) return HttpJson.Bad(req, "Invalid body");

            var orders = new TableClient(_conn, _ordersTable);
            try
            {
                var resp = await orders.GetEntityAsync<OrderEntity>("Order", id);
                var e = resp.Value;

                // Update fields that can be changed
                if (input.TryGetValue("Quantity", out var qty) && qty != null)
                {
                    var newQty = Convert.ToInt32(qty);
                    e.Quantity = newQty;
                    // Recalculate total based on quantity change
                    e.TotalPrice = e.UnitPrice * newQty;
                }

                if (input.TryGetValue("Status", out var status) && status != null)
                    e.Status = status.ToString() ?? e.Status;

                if (input.TryGetValue("OrderDate", out var orderDate) && orderDate != null)
                {
                    if (orderDate is string dateStr && DateTimeOffset.TryParse(dateStr, out var dt))
                        e.OrderDateUtc = dt.ToUniversalTime();
                    else if (orderDate is DateTime dateTime)
                        e.OrderDateUtc = new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc));
                    else if (orderDate is DateTimeOffset dto)
                        e.OrderDateUtc = dto.ToUniversalTime();
                }

                await orders.UpdateEntityAsync(e, e.ETag, TableUpdateMode.Replace);
                return HttpJson.Ok(req, Map.ToDto(e));
            }
            catch (Exception ex)
            {
                return HttpJson.Bad(req, $"Error updating order: {ex.Message}");
            }
        }

    }
    }
