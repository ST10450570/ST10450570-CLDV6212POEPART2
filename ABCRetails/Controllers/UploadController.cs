using ABCRetails.Models;
using ABCRetails.Services;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace ABCRetails.Controllers
{
    public class UploadController : Controller
    {
        private readonly IFunctionsApiService _functionsApiService;

        public UploadController(IFunctionsApiService functionsApiService)
        {
            _functionsApiService = functionsApiService;
        }

        public IActionResult Index()
        {
            return View(new FileUploadModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Index(FileUploadModel model)
        {
            if (ModelState.IsValid)
            {
                try
                {
                    if (model.ProofOfPayment != null && model.ProofOfPayment.Length > 0)
                    {
                        var fileName = await _functionsApiService.UploadProofOfPaymentAsync(
                            model.ProofOfPayment, model.OrderId, model.CustomerName);

                        TempData["Success"] = $"File uploaded successfully! File name: {fileName}";
                        return View(new FileUploadModel());
                    }
                    else
                    {
                        ModelState.AddModelError("ProofOfPayment", "Please select a file to upload.");
                    }
                }
                catch (Exception ex)
                {
                    ModelState.AddModelError("", $"Error uploading file: {ex.Message}");
                }
            }
            return View(model);
        }
    }
}