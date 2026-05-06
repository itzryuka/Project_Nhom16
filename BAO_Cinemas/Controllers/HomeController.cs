using System.Diagnostics; // Thư viện để theo dõi hoạt động hệ thống và tạo RequestId cho trang lỗi
using BAO_Cinemas.Models; // Sử dụng các lớp (class) Database (Movie, Cinema, v.v.)
using Microsoft.EntityFrameworkCore; // Thư viện ORM dùng để tương tác với Database (Include, ToListAsync...)
using Microsoft.AspNetCore.Mvc; // Thư viện cốt lõi của MVC (Model-View-Controller) trong ASP.NET Core

namespace BAO_Cinemas.Controllers
{
    // HomeController là lớp điều khiển mặc định, xử lý các trang chung như Trang chủ, Chính sách bảo mật, Lỗi.
    public class HomeController : Controller
    {
        // Biến dùng để ghi lại lịch sử hoạt động (logs), rất hữu ích khi dò lỗi.
        private readonly ILogger<HomeController> _logger;

        // Biến chứa "đường ống" kết nối với cơ sở dữ liệu SQL Server.
        private readonly ApplicationDbContext _context;

        // Hàm khởi tạo (Constructor):
        // Khi người dùng vào web, hệ thống tự động tạo HomeController và "tiêm" (inject) sẵn logger và context vào đây.
        public HomeController(ILogger<HomeController> logger, ApplicationDbContext context)
        {
            _logger = logger;
            _context = context;
        }

        // Action xử lý giao diện Trang Chủ
        // Hàm này chạy bất đồng bộ (async) để web không bị treo khi chờ lấy dữ liệu từ DB.
        public async Task<IActionResult> Index()
        {
            // Truy vấn lấy dữ liệu Phim, bao gồm luôn cả thông tin Suất chiếu, Rạp và Phòng.
            var movies = await _context.Movies
                .Include(m => m.Showtimes)       // Lấy danh sách suất chiếu của từng bộ phim
                    .ThenInclude(s => s.Room)    // Từ suất chiếu, lấy thông tin Phòng chiếu
                .Include(m => m.Showtimes)       // Phải gọi lại Include trước khi nhảy sang nhánh ThenInclude khác
                    .ThenInclude(s => s.Cinema)  // Từ suất chiếu, lấy thông tin Rạp phim
                .ToListAsync();                  // Thực thi câu lệnh SQL và lưu kết quả vào một danh sách.

            // Chuyển danh sách phim (movies) sang cho file giao diện (Views/Home/Index.cshtml) để hiển thị.
            return View(movies);
        }

        // Action xử lý giao diện trang Chính sách bảo mật (Privacy Policy)
        public IActionResult Privacy()
        {
            return View(); // Trả về giao diện Views/Home/Privacy.cshtml
        }

        // Action xử lý trang hiển thị Lỗi (Error)
        // [ResponseCache...] đảm bảo trình duyệt không lưu bộ nhớ đệm trang lỗi này, luôn hiển thị lỗi mới nhất.
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            // Tạo đối tượng ErrorViewModel chứa mã lỗi (RequestId)
            // Mã này giúp lập trình viên tìm nguyên nhân gây lỗi trong file log hệ thống.
            var errorModel = new ErrorViewModel
            {
                RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier
            };

            return View(errorModel); // Trả về giao diện Views/Shared/Error.cshtml
        }
    }
}