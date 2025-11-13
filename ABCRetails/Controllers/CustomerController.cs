using ABCRetails.Models;
using ABCRetails.Services;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;

namespace ABCRetails.Controllers
{
    [Authorize(Roles = "Admin")]
    public class CustomerController : Controller
    {
        private readonly IFunctionsApiService _functionsApiService;

        public CustomerController(IFunctionsApiService functionsApiService)
        {
            _functionsApiService = functionsApiService;
        }

        public async Task<IActionResult> Index(string searchTerm)
        {
            var customers = string.IsNullOrEmpty(searchTerm)
                ? await _functionsApiService.GetAllCustomersAsync()
                : await _functionsApiService.SearchCustomersAsync(searchTerm);

            ViewBag.SearchTerm = searchTerm;
            return View(customers);
        }

        public IActionResult Create()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Customer customer)
        {
            if (ModelState.IsValid)
            {
                try
                {
                    await _functionsApiService.CreateCustomerAsync(customer);
                    TempData["Success"] = "Customer created successfully!";
                    return RedirectToAction(nameof(Index));
                }
                catch (Exception ex)
                {
                    ModelState.AddModelError("", $"Error creating customer: {ex.Message}");
                }
            }
            return View(customer);
        }

        public async Task<IActionResult> Edit(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return NotFound();
            }
            var customer = await _functionsApiService.GetCustomerAsync(id);
            if (customer == null)
            {
                return NotFound();
            }
            return View(customer);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(string id, Customer customer)
        {
            if (id != customer.RowKey)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    // Get the existing customer to preserve system properties
                    var existingCustomer = await _functionsApiService.GetCustomerAsync(id);
                    if (existingCustomer == null)
                    {
                        TempData["Error"] = "Customer not found. It may have been deleted.";
                        return RedirectToAction(nameof(Index));
                    }

                    // Preserve the system properties
                    customer.PartitionKey = existingCustomer.PartitionKey;
                    customer.RowKey = existingCustomer.RowKey;
                    customer.Timestamp = existingCustomer.Timestamp;
                    customer.ETag = existingCustomer.ETag;

                    await _functionsApiService.UpdateCustomerAsync(customer);
                    TempData["Success"] = "Customer updated successfully!";
                    return RedirectToAction(nameof(Index));
                }
                catch (Exception ex)
                {
                    ModelState.AddModelError("", $"Error updating customer: {ex.Message}");
                }
            }
            return View(customer);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(string id)
        {
            try
            {
                await _functionsApiService.DeleteCustomerAsync(id);
                TempData["Success"] = "Customer deleted successfully!";
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Error deleting customer: {ex.Message}";
            }
            return RedirectToAction(nameof(Index));
        }
    }
}