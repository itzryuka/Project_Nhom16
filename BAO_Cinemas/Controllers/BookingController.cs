using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore; // Dùng để thao tác với DB (Include, ToList...)
using BAO_Cinemas.Models;
using Microsoft.AspNetCore.Authorization; // Dùng để phân quyền (bắt buộc đăng nhập)
using System.Security.Claims; // Dùng để trích xuất thông tin User (như ID) từ Cookie/Token đăng nhập

namespace BAO_Cinemas.Controllers
{
    // [Authorize] là "Bảo vệ": Bắt buộc người dùng phải Đăng nhập thì mới được vào bất kỳ trang nào trong Controller này.
    // Nếu chưa đăng nhập, hệ thống sẽ tự động đá văng sang trang Login.
    [Authorize]
    public class BookingController : Controller
    {
        private readonly ApplicationDbContext _context;

        // Dependency Injection: Nhận ống kết nối Database từ hệ thống
        public BookingController(ApplicationDbContext context)
        {
            _context = context;
        }

        // =========================================================
        // 1. TRANG SƠ ĐỒ CHỌN GHẾ (Giao diện hiển thị ghế Trống/Đã đặt)
        // =========================================================
        public IActionResult SeatSelection(int id)
        {
            // Lấy thông tin Suất chiếu (Showtime) kèm theo Phim, Rạp và Phòng
            var showtime = _context.Showtimes
                .Include(s => s.Movie).Include(s => s.Cinema).Include(s => s.Room)
                .FirstOrDefault(s => s.Id == id);

            if (showtime == null) return NotFound();

            // LOGIC TÌM GHẾ ĐÃ ĐẶT (Đã nâng cấp theo DB mới):
            // Thay vì đọc chuỗi string, ta chui vào bảng BookingSeats, 
            // tìm các ghế thuộc suất chiếu này (ShowtimeId == id) và đã thanh toán (Status == "confirmed").
            // Sau đó Select để chỉ lấy ra Mã ghế (VD: "A1", "A2") trả về cho giao diện.
            var bookedSeatCodes = _context.BookingSeats
                .Where(bs => bs.ShowtimeId == id && bs.Status == "confirmed")
                .Select(bs => bs.Seat.SeatCode)
                .ToList();

            // Truyền danh sách mã ghế đã đặt sang View để tô màu xám/đỏ (Không cho chọn)
            ViewBag.BookedSeatCodes = bookedSeatCodes;

            return View(showtime);
        }

        // =========================================================
        // 2. XỬ LÝ THANH TOÁN & LƯU DATABASE (LUỒNG LÕI)
        // =========================================================
        [HttpPost] // Chỉ nhận request dạng POST (khi người dùng bấm nút Submit Đặt vé)
        public IActionResult ConfirmBooking(int showtimeId, string selectedSeats)
        {
            // Kiểm tra: Nếu khách chưa chọn ghế nào mà đã bấm đặt vé thì đuổi về lại trang chọn ghế
            if (string.IsNullOrEmpty(selectedSeats))
                return RedirectToAction("SeatSelection", new { id = showtimeId });

            var showtime = _context.Showtimes.Include(s => s.Room).FirstOrDefault(s => s.Id == showtimeId);
            if (showtime == null) return NotFound();

            // Frontend gửi lên chuỗi "A1,A2,B3". Cắt chuỗi này ra thành 1 mảng List: ["A1", "A2", "B3"]
            var requestedSeatCodes = selectedSeats.Split(',').Select(s => s.Trim()).ToList();

            // BƯỚC 1: XÁC THỰC GHẾ (Validation)
            // Truy vấn bảng Seats để tìm ID thực sự của các ghế này trong Phòng đó.
            var seats = _context.Seats
                .Where(s => s.RoomId == showtime.RoomId && requestedSeatCodes.Contains(s.SeatCode) && s.Status == "active")
                .ToList();

            // Nếu số ghế tìm thấy trong DB không khớp với số ghế khách chọn -> Có người hack HTML hoặc lỗi
            if (seats.Count != requestedSeatCodes.Count)
                return Content("Một hoặc nhiều ghế không hợp lệ!");

            // BƯỚC 2: KIỂM TRA TRANH CHẤP (Concurrency / Conflict Check) - Cực kỳ quan trọng
            // Lấy danh sách ID của các ghế khách vừa chọn
            var seatIds = seats.Select(s => s.Id).ToList();

            // Tìm trong bảng BookingSeats xem có ghế nào trong danh sách trên ĐÃ BỊ NGƯỜI KHÁC ĐẶT MẤT trong suất chiếu này không?
            var conflicted = _context.BookingSeats
                .Where(bs => bs.ShowtimeId == showtimeId
                          && bs.Status == "confirmed"
                          && seatIds.Contains(bs.SeatId))
                .Select(bs => bs.Seat.SeatCode) // Lấy ra tên ghế bị trùng để báo lỗi
                .ToList();

            // Nếu có bất kỳ ghế nào bị trùng, lập tức chặn lại và báo lỗi.
            if (conflicted.Any())
                return Content($"Rất tiếc, ghế {string.Join(", ", conflicted)} đã có người đặt!");

            // BƯỚC 3: TẠO HÓA ĐƠN TỔNG (Bảng Booking)
            // Lấy ID của người dùng đang đăng nhập hiện tại
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            var booking = new Booking
            {
                ShowtimeId = showtimeId,
                UserId = userId,
                TotalPrice = seats.Count * showtime.Price, // Tổng tiền = Số ghế x Giá 1 vé
                BookingTime = DateTime.Now,
                Status = "confirmed"
            };

            _context.Bookings.Add(booking);
            _context.SaveChanges(); // LƯU LẦN 1: Bắt buộc lưu để SQL Server sinh ra mã Hóa Đơn (booking.Id)

            // BƯỚC 4: TẠO CHI TIẾT VÉ CHO TỪNG GHẾ (Bảng BookingSeat)
            // Duyệt qua từng ghế khách chọn, tạo 1 bản ghi BookingSeat nối với Hóa đơn tổng ở trên.
            var bookingSeats = seats.Select(seat => new BookingSeat
            {
                BookingId = booking.Id, // Nối với ID hóa đơn vừa lưu
                SeatId = seat.Id,
                ShowtimeId = showtimeId,
                Status = "confirmed"
            }).ToList();

            _context.BookingSeats.AddRange(bookingSeats); // AddRange để thêm nhiều bản ghi cùng lúc (Tối ưu hiệu suất)
            _context.SaveChanges(); // LƯU LẦN 2: Chốt hạ dữ liệu xuống DB

            // Chuyển hướng sang trang Thành công, mang theo mã Hóa đơn
            return RedirectToAction("Success", new { bookingId = booking.Id });
        }

        // =========================================================
        // 3. TRANG HIỂN THỊ VÉ ĐIỆN TỬ (Success)
        // =========================================================
        public IActionResult Success(int bookingId)
        {
            // Lấy thông tin Hóa đơn (Booking), Include (kéo theo) tất cả thông tin rễ nhánh:
            // Booking -> Showtime -> Movie (Phim)
            // Booking -> Showtime -> Cinema (Rạp)
            // Booking -> Showtime -> Room (Phòng)
            var booking = _context.Bookings
                .Include(b => b.Showtime).ThenInclude(s => s.Movie)
                .Include(b => b.Showtime).ThenInclude(s => s.Cinema)
                .Include(b => b.Showtime).ThenInclude(s => s.Room)
                .FirstOrDefault(b => b.Id == bookingId);

            if (booking == null) return NotFound();

            // Gửi dữ liệu ra view Success.cshtml để in Vé điện tử (QR Code, Tên phim, Rạp...)
            return View(booking);
        }
    }
}