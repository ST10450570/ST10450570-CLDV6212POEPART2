using System.Diagnostics;
using ABCRetails.Models;
using ABCRetails.Models.ViewModels;
using ABCRetails.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ABCRetails.Controllers
{
    public class HomeController : Controller
    {
        private readonly IFunctionsApiService _functionsApiService;

        public HomeController(IFunctionsApiService functionsApiService)
        {
            _functionsApiService = functionsApiService;
        }

        public async Task<IActionResult> Index()
        {
            var products = await _functionsApiService.GetAllProductsAsync();
            var customers = await _functionsApiService.GetAllCustomersAsync();
            var orders = await _functionsApiService.GetAllOrdersAsync();

            var viewModel = new HomeViewModel
            {
                FeaturedProducts = products.Take(5).ToList(),
                ProductCount = products.Count,
                CustomerCount = customers.Count,
                OrderCount = orders.Count
            };
            return View(viewModel);
        }

        public IActionResult Privacy()
        {
            return View();
        }

        public IActionResult Contact()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel
            {
                RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier
            });
        }

        // Add these methods to the existing HomeController class

        [Authorize(Roles = "Admin")]
        public IActionResult AdminDashboard()
        {
            return View();
        }

        [Authorize(Roles = "Customer")]
        public IActionResult CustomerDashboard()
        {
            return View();
        }

        [Authorize]
        public IActionResult AccessDenied()
        {
            return View();
        }
    }
}