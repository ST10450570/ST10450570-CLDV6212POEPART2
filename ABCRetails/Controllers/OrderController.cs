using System.Security.Claims;
using System.Threading.Tasks;
using ABCRetails.Data;
using ABCRetails.Models;
using ABCRetails.Models.ViewModels;
using ABCRetails.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging; // Add this using statement

namespace ABCRetails.Controllers
{
    public class OrderController : Controller
    {
        private readonly IFunctionsApiService _functionsApiService;
        private readonly AuthDbContext _authContext;
        private readonly ILogger<OrderController> _logger; // Add this field

        public OrderController(
            IFunctionsApiService functionsApiService,
            AuthDbContext authContext,
            ILogger<OrderController> logger) // Add this parameter
        {
            _functionsApiService = functionsApiService;
            _authContext = authContext;
            _logger = logger; // Initialize the logger
        }

        [Authorize]
        public async Task<IActionResult> Index(string searchTerm)
        {
            var orders = string.IsNullOrEmpty(searchTerm)
                ? await _functionsApiService.GetAllOrdersAsync()
                : await _functionsApiService.SearchOrdersAsync(searchTerm);

            // If user is customer, only show their orders
            if (User.IsInRole("Customer"))
            {
                var currentUsername = User.FindFirst(ClaimTypes.Name)?.Value;
                orders = orders.Where(o => o.Username == currentUsername).ToList();
            }

            ViewBag.SearchTerm = searchTerm;
            return View(orders);
        }

        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Create()
        {
            var customers = await _functionsApiService.GetAllCustomersAsync();
            var products = await _functionsApiService.GetAllProductsAsync();

            var viewModel = new OrderCreateViewModel
            {
                Customers = customers,
                Products = products,
                OrderDate = DateTime.SpecifyKind(DateTime.UtcNow.Date, DateTimeKind.Utc)
            };
            return View(viewModel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Create(OrderCreateViewModel model)
        {
            // Remove validation for fields we don't need to validate
            ModelState.Remove("Customers");
            ModelState.Remove("Products");

            if (ModelState.IsValid)
            {
                try
                {
                    var customer = await _functionsApiService.GetCustomerAsync(model.CustomerId);
                    var product = await _functionsApiService.GetProductAsync(model.ProductId);

                    if (customer == null || product == null)
                    {
                        ModelState.AddModelError("", "Invalid customer or product selected.");
                        await PopulateDropdowns(model);
                        return View(model);
                    }

                    if (product.StockAvailable < model.Quantity)
                    {
                        ModelState.AddModelError("Quantity",
                            $"Insufficient stock. Available: {product.StockAvailable}");
                        await PopulateDropdowns(model);
                        return View(model);
                    }

                    var utcOrderDate = DateTime.SpecifyKind(model.OrderDate, DateTimeKind.Utc);

                    var order = new Order
                    {
                        RowKey = Guid.NewGuid().ToString(),
                        CustomerId = model.CustomerId,
                        Username = customer.Username,
                        ProductId = model.ProductId,
                        ProductName = product.ProductName,
                        Quantity = model.Quantity,
                        OrderDate = utcOrderDate,
                        UnitPrice = product.Price,
                        TotalPrice = product.Price * model.Quantity,
                        Status = model.Status, // This should now work
                        ProductImageUrl = product.ImageUrl
                    };

                    _logger.LogInformation("Creating order with status: {Status}", order.Status);

                    await _functionsApiService.CreateOrderAsync(order);
                    TempData["Success"] = "Order created successfully!";
                    return RedirectToAction(nameof(Index));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error creating order");
                    ModelState.AddModelError("", $"Error creating order: {ex.Message}");
                }
            }

            await PopulateDropdowns(model);
            return View(model);
        }

        [Authorize]
        public async Task<IActionResult> Details(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return NotFound();
            }
            var order = await _functionsApiService.GetOrderAsync(id);
            if (order == null)
            {
                return NotFound();
            }

            // Customers can only see their own orders
            if (User.IsInRole("Customer"))
            {
                var currentUsername = User.FindFirst(ClaimTypes.Name)?.Value;
                if (order.Username != currentUsername)
                {
                    return RedirectToAction("AccessDenied", "Home");
                }
            }

            return View(order);
        }

        [HttpPost]
        [Authorize(Roles = "Admin")]
        public async Task<JsonResult> UpdateOrderStatus(string id, string newStatus)
        {
            try
            {
                var success = await _functionsApiService.UpdateOrderStatusAsync(id, newStatus);
                if (success)
                {
                    _logger.LogInformation("Order {OrderId} status updated to {Status}", id, newStatus);
                    return Json(new { success = true, message = "Order status updated successfully!" });
                }
                return Json(new { success = false, message = "Failed to update order status" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating order {OrderId} status to {Status}", id, newStatus);
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(string id)
        {
            try
            {
                await _functionsApiService.DeleteOrderAsync(id);
                TempData["Success"] = "Order deleted successfully!";
                _logger.LogInformation("Order {OrderId} deleted successfully", id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting order {OrderId}", id);
                TempData["Error"] = $"Error deleting order: {ex.Message}";
            }
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        [Authorize]
        public async Task<JsonResult> GetProductPrice(string productId)
        {
            try
            {
                var product = await _functionsApiService.GetProductAsync(productId);
                if (product != null)
                {
                    return Json(new
                    {
                        success = true,
                        price = product.Price,
                        stock = product.StockAvailable,
                        productName = product.ProductName,
                    });
                }

                return Json(new { success = false });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting product price for {ProductId}", productId);
                return Json(new { success = false });
            }
        }

        [HttpGet]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Edit(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return NotFound();
            }

            var order = await _functionsApiService.GetOrderAsync(id);
            if (order == null)
            {
                return NotFound();
            }

            var viewModel = new OrderEditViewModel
            {
                Id = order.RowKey,
                Status = order.Status,
                Order = order,
                Customers = await _functionsApiService.GetAllCustomersAsync(),
                Products = await _functionsApiService.GetAllProductsAsync(),
                StatusOptions = new List<string> { "Submitted", "Processing", "Completed", "Cancelled" }
            };

            return View(viewModel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Edit(string id, OrderEditViewModel model)
        {
            if (id != model.Id)
            {
                return NotFound();
            }

            // Remove ModelState validation for system properties
            ModelState.Remove("Order.PartitionKey");
            ModelState.Remove("Order.RowKey");
            ModelState.Remove("Order.Timestamp");
            ModelState.Remove("Order.ETag");

            if (ModelState.IsValid)
            {
                try
                {
                    // Get the original order to preserve system properties
                    var originalOrder = await _functionsApiService.GetOrderAsync(id);
                    if (originalOrder == null)
                    {
                        TempData["Error"] = "Order not found. It may have been deleted.";
                        return RedirectToAction(nameof(Index));
                    }

                    // Update ONLY the editable fields
                    originalOrder.Quantity = model.Order.Quantity;
                    originalOrder.Status = model.Order.Status;
                    originalOrder.OrderDate = DateTime.SpecifyKind(model.Order.OrderDate, DateTimeKind.Utc);

                    // Recalculate total price
                    originalOrder.TotalPrice = originalOrder.UnitPrice * model.Order.Quantity;

                    // Update the order using the service
                    await _functionsApiService.UpdateOrderAsync(originalOrder);

                    _logger.LogInformation("Order {OrderId} updated successfully", id);
                    TempData["Success"] = "Order updated successfully!";
                    return RedirectToAction(nameof(Index));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error updating order {OrderId}", id);
                    ModelState.AddModelError("", $"Error updating order: {ex.Message}");
                }
            }

            // Repopulate dropdowns if there was an error
            model.Customers = await _functionsApiService.GetAllCustomersAsync();
            model.Products = await _functionsApiService.GetAllProductsAsync();
            model.StatusOptions = new List<string> { "Submitted", "Processing", "Completed", "Cancelled" };
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> ProcessOrder(string id)
        {
            try
            {
                var success = await _functionsApiService.UpdateOrderStatusAsync(id, "PROCESSED");
                if (success)
                {
                    _logger.LogInformation("Order {OrderId} marked as PROCESSED", id);
                    TempData["Success"] = "Order marked as PROCESSED successfully!";
                }
                else
                {
                    TempData["Error"] = "Failed to update order status.";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing order {OrderId}", id);
                TempData["Error"] = $"Error processing order: {ex.Message}";
            }
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        [Authorize(Roles = "Customer")]
        public async Task<IActionResult> MyOrders()
        {
            var currentUsername = User.FindFirst(ClaimTypes.Name)?.Value;
            var allOrders = await _functionsApiService.GetAllOrdersAsync();
            var myOrders = allOrders.Where(o => o.Username == currentUsername).ToList();

            return View(myOrders);
        }

        private async Task PopulateDropdowns(OrderCreateViewModel model)
        {
            model.Customers = await _functionsApiService.GetAllCustomersAsync();
            model.Products = await _functionsApiService.GetAllProductsAsync();
        }
    }
}