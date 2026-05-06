using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BAO_Cinemas.Models;

namespace BAO_Cinemas.Controllers
{
    // Khởi tạo class MoviesController kế thừa từ Controller của ASP.NET Core MVC
    public class MoviesController : Controller
    {
        // Biến nội bộ (chỉ đọc) dùng để chứa "ống kết nối" tới cơ sở dữ liệu
        private readonly ApplicationDbContext _context;

        // Constructor: Hàm này chạy đầu tiên khi Controller được gọi.
        // Hệ thống (Dependency Injection) sẽ tự động "tiêm" ApplicationDbContext vào đây.
        public MoviesController(ApplicationDbContext context)
        {
            // Gán context được truyền vào cho biến nội bộ để sử dụng ở các hàm bên dưới
            _context = context;
        }

        // Action xử lý giao diện cho trang "Kho Phim" (đường dẫn: /Movies/MovieStorage)
        // Dùng 'async Task' để xử lý bất đồng bộ, giúp web không bị đơ khi chờ gọi database
        public async Task<IActionResult> MovieStorage()
        {
            // Bước 1: _context.Movies -> Truy cập vào bảng Movies trong Database.
            // Bước 2: .OrderByDescending(m => m.Id) -> Sắp xếp phim theo ID giảm dần (Phim mới nhất lên đầu).
            // Bước 3: .ToListAsync() -> Lấy dữ liệu từ SQL và chuyển thành danh sách (List).
            // Lệnh 'await' sẽ đợi đến khi lấy xong toàn bộ dữ liệu mới chạy tiếp.
            var movies = await _context.Movies.OrderByDescending(m => m.Id).ToListAsync();

            // Gửi danh sách 'movies' vừa lấy được sang giao diện View (file MovieStorage.cshtml) để hiển thị lên Web.
            return View(movies);
        }
    }
}