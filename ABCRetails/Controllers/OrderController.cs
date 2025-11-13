using System.Threading.Tasks;
using ABCRetails.Models;
using ABCRetails.Models.ViewModels;
using ABCRetails.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ABCRetails.Controllers
{
    public class OrderController : Controller
    {
        private readonly IFunctionsApiService _functionsApiService;

        public OrderController(IFunctionsApiService functionsApiService)
        {
            _functionsApiService = functionsApiService;
        }

        public async Task<IActionResult> Index(string searchTerm)
        {
            var orders = string.IsNullOrEmpty(searchTerm)
                ? await _functionsApiService.GetAllOrdersAsync()
                : await _functionsApiService.SearchOrdersAsync(searchTerm);

            ViewBag.SearchTerm = searchTerm;
            return View(orders);
        }

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
        public async Task<IActionResult> Create(OrderCreateViewModel model)
        {
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
                        Status = model.Status,
                        ProductImageUrl = product.ImageUrl // Include product image URL
                    };

                    await _functionsApiService.CreateOrderAsync(order);
                    TempData["Success"] = "Order created successfully!";
                    return RedirectToAction(nameof(Index));
                }
                catch (Exception ex)
                {
                    ModelState.AddModelError("", $"Error creating order: {ex.Message}");
                }
            }

            await PopulateDropdowns(model);
            return View(model);
        }

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
            return View(order);
        }

        [HttpPost]
        public async Task<JsonResult> UpdateOrderStatus(string id, string newStatus)
        {
            try
            {
                var success = await _functionsApiService.UpdateOrderStatusAsync(id, newStatus);
                if (success)
                {
                    return Json(new { success = true, message = "Order status updated successfully!" });
                }
                return Json(new { success = false, message = "Failed to update order status" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(string id)
        {
            try
            {
                await _functionsApiService.DeleteOrderAsync(id);
                TempData["Success"] = "Order deleted successfully!";
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Error deleting order: {ex.Message}";
            }
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
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
            catch
            {
                return Json(new { success = false });
            }
        }

        [HttpGet]
        public async Task<JsonResult> GetProductImage(string productId)
        {
            try
            {
                var product = await _functionsApiService.GetProductAsync(productId);
                if (product != null && !string.IsNullOrEmpty(product.ImageUrl))
                {
                    return Json(new
                    {
                        success = true,
                        imageUrl = product.ImageUrl
                    });
                }

                return Json(new { success = false });
            }
            catch
            {
                return Json(new { success = false });
            }
        }

        [HttpGet]
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
                StatusOptions = new List<string> { "Submitted", "Processing", "Completed", "Cancelled" } // Match your actual status values
            };

            return View(viewModel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(string id, OrderEditViewModel model)
        {
            if (id != model.Id)
            {
                return NotFound();
            }

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

                    // Update only the fields that should be editable
                    originalOrder.Quantity = model.Order.Quantity;
                    originalOrder.Status = model.Order.Status;
                    originalOrder.OrderDate = model.Order.OrderDate;

                    // Preserve system properties
                    originalOrder.PartitionKey = "Order"; // Ensure this is always set
                    originalOrder.RowKey = model.Id;

                    await _functionsApiService.UpdateOrderAsync(originalOrder);

                    TempData["Success"] = "Order updated successfully!";
                    return RedirectToAction(nameof(Index));
                }
                catch (Exception ex)
                {
                    ModelState.AddModelError("", $"Error updating order: {ex.Message}");
                }
            }

            // Repopulate dropdowns if there was an error
            model.Customers = await _functionsApiService.GetAllCustomersAsync();
            model.Products = await _functionsApiService.GetAllProductsAsync();
            model.StatusOptions = new List<string> { "Submitted", "Processing", "Completed", "Cancelled" };
            return View(model);
        }

        private async Task PopulateDropdowns(OrderCreateViewModel model)
        {
            model.Customers = await _functionsApiService.GetAllCustomersAsync();
            model.Products = await _functionsApiService.GetAllProductsAsync();
        }

        // Add this method to the existing OrderController class
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
                    TempData["Success"] = "Order marked as PROCESSED successfully!";
                }
                else
                {
                    TempData["Error"] = "Failed to update order status.";
                }
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Error processing order: {ex.Message}";
            }
            return RedirectToAction(nameof(Index));
        }
    }
}