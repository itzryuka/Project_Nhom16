using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BAO_Cinemas.Models;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using System.IO;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BAO_Cinemas.Controllers
{
    /// <summary>
    /// Chức năng: Lớp Controller quản lý toàn bộ nghiệp vụ Backend dành cho Admin 
    /// (Bao gồm: Phim, Rạp, Phòng chiếu, Suất chiếu, Doanh thu).
    /// </summary>
    public class ManagementController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _webHostEnvironment;

        /// <summary>
        /// Chức năng: Khởi tạo Controller, tiêm (inject) các dịch vụ cần thiết.
        /// </summary>
        /// <param name="context">Ống kết nối cơ sở dữ liệu.</param>
        /// <param name="webHostEnvironment">Môi trường host để xử lý lưu file ảnh.</param>
        public ManagementController(ApplicationDbContext context, IWebHostEnvironment webHostEnvironment)
        {
            _context = context;
            _webHostEnvironment = webHostEnvironment;
        }

        // =================================================================
        // ======================= 1. MODULE QUẢN LÝ PHIM ==================
        // =================================================================

        /// <summary>
        /// Chức năng: Hiển thị danh sách toàn bộ phim.
        /// </summary>
        /// <returns>View chứa danh sách List&lt;Movie&gt;.</returns>
        public async Task<IActionResult> Movie()
        {
            var movies = await _context.Movies.ToListAsync();
            return View(movies);
        }

        /// <summary>
        /// Chức năng: Hiển thị form thêm phim mới.
        /// </summary>
        /// <returns>View form tạo phim.</returns>
        [HttpGet]
        public IActionResult CreateMovie() => View();

        /// <summary>
        /// Chức năng: Xử lý lưu thông tin phim mới và upload ảnh poster.
        /// </summary>
        /// <param name="movie">Dữ liệu phim từ form.</param>
        /// <param name="PosterFile">File ảnh tải lên.</param>
        /// <returns>Chuyển hướng về trang danh sách phim (nếu thành công) hoặc trả lại View kèm lỗi.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateMovie(Movie movie, IFormFile PosterFile)
        {
            ModelState.Remove("Showtimes");
            ModelState.Remove("PosterUrl");
            if (ModelState.IsValid)
            {
                if (PosterFile != null && PosterFile.Length > 0)
                {
                    string fileName = Guid.NewGuid().ToString() + "_" + Path.GetFileName(PosterFile.FileName);
                    string uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "images", "posters");
                    if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);
                    string filePath = Path.Combine(uploadsFolder, fileName);
                    using (var fileStream = new FileStream(filePath, FileMode.Create))
                        await PosterFile.CopyToAsync(fileStream);
                    movie.PosterUrl = "/images/posters/" + fileName;
                }
                else
                {
                    movie.PosterUrl = "/images/posters/default.jpg";
                }
                _context.Movies.Add(movie);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Movie));
            }
            return View(movie);
        }

        /// <summary>
        /// Chức năng: Hiển thị form chỉnh sửa phim.
        /// </summary>
        /// <param name="id">Mã phim.</param>
        /// <returns>View chỉnh sửa kèm dữ liệu phim, hoặc NotFound nếu không tìm thấy.</returns>
        [HttpGet]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();
            var movie = await _context.Movies.FindAsync(id);
            if (movie == null) return NotFound();
            return View(movie);
        }

        /// <summary>
        /// Chức năng: Xử lý cập nhật thông tin phim và thay đổi ảnh poster (nếu có).
        /// </summary>
        /// <param name="id">Mã phim.</param>
        /// <param name="movie">Dữ liệu mới.</param>
        /// <param name="PosterFile">Ảnh mới (tùy chọn).</param>
        /// <returns>Chuyển hướng về trang danh sách phim hoặc View lỗi.</returns>
        /// <exception cref="Exception">Bắt lỗi khi thao tác DB hoặc lưu file, ghi lỗi vào ModelState.</exception>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Movie movie, IFormFile? PosterFile)
        {
            if (id != movie.Id) return NotFound();
            ModelState.Remove("Showtimes");
            ModelState.Remove("PosterUrl");
            if (ModelState.IsValid)
            {
                try
                {
                    var existingMovie = await _context.Movies.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id);
                    if (existingMovie == null) return NotFound();

                    if (PosterFile != null && PosterFile.Length > 0)
                    {
                        string fileName = Guid.NewGuid().ToString() + "_" + Path.GetFileName(PosterFile.FileName);
                        string uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "images", "posters");
                        string filePath = Path.Combine(uploadsFolder, fileName);
                        using (var fileStream = new FileStream(filePath, FileMode.Create))
                            await PosterFile.CopyToAsync(fileStream);
                        movie.PosterUrl = "/images/posters/" + fileName;
                    }
                    else
                    {
                        movie.PosterUrl = existingMovie.PosterUrl;
                    }
                    _context.Update(movie);
                    await _context.SaveChangesAsync();
                    return RedirectToAction(nameof(Movie));
                }
                catch (Exception ex)
                {
                    ModelState.AddModelError("", "Lỗi cập nhật: " + ex.Message);
                }
            }
            return View(movie);
        }

        /// <summary>
        /// Chức năng: Xóa một bộ phim và file ảnh vật lý tương ứng. Chặn xóa nếu phim đang có suất chiếu.
        /// </summary>
        /// <param name="id">Mã phim.</param>
        /// <returns>Chuyển hướng về trang danh sách phim kèm thông báo.</returns>
        /// <exception cref="Exception">Bắt lỗi khi xóa file ảnh hoặc lỗi DB, ghi thông báo vào TempData.</exception>
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();
            var movie = await _context.Movies.Include(m => m.Showtimes).FirstOrDefaultAsync(m => m.Id == id);
            if (movie == null) return NotFound();

            if (movie.Showtimes != null && movie.Showtimes.Any())
            {
                TempData["ErrorMessage"] = $"Không thể xóa phim '{movie.Title}' vì đang có {movie.Showtimes.Count} suất chiếu.";
                return RedirectToAction(nameof(Movie));
            }
            try
            {
                if (!string.IsNullOrEmpty(movie.PosterUrl) && movie.PosterUrl != "/images/posters/default.jpg")
                {
                    var imagePath = Path.Combine(_webHostEnvironment.WebRootPath, movie.PosterUrl.TrimStart('/'));
                    if (System.IO.File.Exists(imagePath)) System.IO.File.Delete(imagePath);
                }
                _context.Movies.Remove(movie);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = $"Đã xóa phim '{movie.Title}' thành công!";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Lỗi hệ thống khi xóa: " + ex.Message;
            }
            return RedirectToAction(nameof(Movie));
        }

        // =================================================================
        // ======================= 2. MODULE QUẢN LÝ RẠP ===================
        // =================================================================

        /// <summary>
        /// Chức năng: Hiển thị danh sách rạp chiếu phim.
        /// </summary>
        /// <returns>View chứa danh sách List&lt;Cinema&gt;.</returns>
        [HttpGet]
        public async Task<IActionResult> Cinema()
        {
            var cinemas = await _context.Cinemas.ToListAsync();
            return View(cinemas);
        }

        /// <summary>
        /// Chức năng: Hiển thị form tạo rạp mới.
        /// </summary>
        /// <returns>View form tạo rạp.</returns>
        [HttpGet]
        public IActionResult CreateCinema() => View();

        /// <summary>
        /// Chức năng: Xử lý lưu thông tin rạp mới.
        /// </summary>
        /// <param name="cinema">Dữ liệu rạp.</param>
        /// <returns>Chuyển hướng hoặc View lỗi.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateCinema(Cinema cinema)
        {
            ModelState.Remove("Rooms");
            if (ModelState.IsValid)
            {
                _context.Cinemas.Add(cinema);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Đã thêm rạp mới thành công!";
                return RedirectToAction(nameof(Cinema));
            }
            return View(cinema);
        }

        /// <summary>
        /// Chức năng: Hiển thị form chỉnh sửa rạp.
        /// </summary>
        /// <param name="id">Mã rạp.</param>
        /// <returns>View form chỉnh sửa.</returns>
        [HttpGet]
        public async Task<IActionResult> EditCinema(int? id)
        {
            if (id == null) return NotFound();
            var cinema = await _context.Cinemas.FindAsync(id);
            if (cinema == null) return NotFound();
            return View(cinema);
        }

        /// <summary>
        /// Chức năng: Xử lý cập nhật thông tin rạp.
        /// </summary>
        /// <param name="id">Mã rạp.</param>
        /// <param name="cinema">Dữ liệu rạp mới.</param>
        /// <returns>Chuyển hướng hoặc View lỗi.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditCinema(int id, Cinema cinema)
        {
            if (id != cinema.Id) return NotFound();
            ModelState.Remove("Rooms");
            if (ModelState.IsValid)
            {
                _context.Update(cinema);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Đã cập nhật thông tin rạp!";
                return RedirectToAction(nameof(Cinema));
            }
            return View(cinema);
        }

        /// <summary>
        /// Chức năng: Xóa rạp chiếu. Chặn xóa nếu rạp đang có phòng chiếu.
        /// </summary>
        /// <param name="id">Mã rạp.</param>
        /// <returns>Chuyển hướng kèm thông báo kết quả.</returns>
        public async Task<IActionResult> DeleteCinema(int? id)
        {
            if (id == null) return NotFound();
            var cinema = await _context.Cinemas.Include(c => c.Rooms).FirstOrDefaultAsync(c => c.Id == id);
            if (cinema == null) return NotFound();

            if (cinema.Rooms != null && cinema.Rooms.Any())
            {
                TempData["ErrorMessage"] = $"Không thể xóa rạp '{cinema.Name}' vì đang có {cinema.Rooms.Count} phòng chiếu.";
                return RedirectToAction(nameof(Cinema));
            }
            _context.Cinemas.Remove(cinema);
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Đã xóa rạp thành công!";
            return RedirectToAction(nameof(Cinema));
        }

        // =================================================================
        // ======================= 3. MODULE QUẢN LÝ PHÒNG & GHẾ ===========
        // =================================================================

        /// <summary>
        /// Chức năng: Hiển thị danh sách phòng của một rạp cụ thể.
        /// </summary>
        /// <param name="cinemaId">Mã rạp.</param>
        /// <returns>View danh sách phòng thuộc rạp.</returns>
        [HttpGet]
        public async Task<IActionResult> RoomList(int? cinemaId)
        {
            if (cinemaId == null) return NotFound("Vui lòng chọn một rạp chiếu.");
            var cinema = await _context.Cinemas.FindAsync(cinemaId);
            if (cinema == null) return NotFound();
            var rooms = await _context.Rooms.Where(r => r.CinemaId == cinemaId).ToListAsync();
            ViewBag.CinemaInfo = cinema;
            return View(rooms);
        }

        /// <summary>
        /// Chức năng: Hiển thị form thêm phòng mới (khởi tạo số hàng, ghế mặc định).
        /// </summary>
        /// <param name="cinemaId">Mã rạp.</param>
        /// <returns>View form tạo phòng.</returns>
        [HttpGet]
        public IActionResult CreateRoom(int? cinemaId)
        {
            if (cinemaId == null) return NotFound("Không xác định được rạp để thêm phòng.");
            var room = new Room { CinemaId = cinemaId.Value, TotalRows = 7, SeatsPerRow = 14, Status = 1 };
            return View(room);
        }

        /// <summary>
        /// Chức năng: Lưu phòng mới và gọi hàm phụ trợ tự sinh sơ đồ ghế.
        /// </summary>
        /// <param name="room">Dữ liệu phòng.</param>
        /// <returns>Chuyển hướng kèm thông báo thành công.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateRoom(Room room)
        {
            ModelState.Remove("Cinema");
            ModelState.Remove("Showtimes");
            ModelState.Remove("Seats");

            if (ModelState.IsValid)
            {
                _context.Rooms.Add(room);
                await _context.SaveChangesAsync();

                await GenerateSeatsForRoom(room.Id, room.TotalRows, room.SeatsPerRow);

                TempData["SuccessMessage"] = $"Đã thêm {room.Name} và tạo {room.TotalRows * room.SeatsPerRow} ghế thành công!";
                return RedirectToAction(nameof(RoomList), new { cinemaId = room.CinemaId });
            }
            return View(room);
        }

        /// <summary>
        /// Chức năng: Hàm phụ trợ tự sinh danh sách ghế (Seat) và lưu DB dựa trên cấu hình hàng/cột của phòng.
        /// </summary>
        /// <param name="roomId">Mã phòng.</param>
        /// <param name="totalRows">Tổng số hàng.</param>
        /// <param name="seatsPerRow">Số ghế mỗi hàng.</param>
        /// <param name="vipRows">Số lượng hàng VIP ở giữa (Mặc định: 2).</param>
        /// <returns>Task bất đồng bộ.</returns>
        private async Task GenerateSeatsForRoom(int roomId, int totalRows, int seatsPerRow, int vipRows = 2)
        {
            var seats = new List<Seat>();
            int midRow = totalRows / 2;

            for (int row = 1; row <= totalRows; row++)
            {
                char rowLabel = (char)(64 + row); // A, B, C...
                string seatType = (row >= midRow - vipRows / 2 && row <= midRow + vipRows / 2)
                    ? "vip" : "standard";

                for (int col = 1; col <= seatsPerRow; col++)
                {
                    seats.Add(new Seat
                    {
                        RoomId = roomId,
                        SeatCode = $"{rowLabel}{col}",
                        RowLabel = rowLabel,
                        SeatNumber = col,
                        SeatType = seatType,
                        Status = "active"
                    });
                }
            }
            _context.Seats.AddRange(seats);
            await _context.SaveChangesAsync();
        }

        /// <summary>
        /// Chức năng: Cập nhật thông tin phòng chiếu (Tên, Trạng thái).
        /// </summary>
        /// <param name="id">Mã phòng.</param>
        /// <returns>View chứa dữ liệu phòng.</returns>
        [HttpGet]
        public async Task<IActionResult> EditRoom(int? id)
        {
            if (id == null) return NotFound();
            var room = await _context.Rooms.FindAsync(id);
            if (room == null) return NotFound();
            return View(room);
        }

        /// <summary>
        /// Chức năng: Xử lý lưu thông tin phòng sau chỉnh sửa.
        /// </summary>
        /// <param name="id">Mã phòng.</param>
        /// <param name="room">Dữ liệu mới.</param>
        /// <returns>Chuyển hướng hoặc View lỗi.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditRoom(int id, Room room)
        {
            if (id != room.Id) return NotFound();
            ModelState.Remove("Cinema");
            ModelState.Remove("Showtimes");
            ModelState.Remove("Seats");
            if (ModelState.IsValid)
            {
                _context.Update(room);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Đã cập nhật thông tin phòng chiếu!";
                return RedirectToAction(nameof(RoomList), new { cinemaId = room.CinemaId });
            }
            return View(room);
        }

        /// <summary>
        /// Chức năng: Xóa phòng chiếu và toàn bộ ghế thuộc phòng đó. Chặn xóa nếu đang có suất chiếu.
        /// </summary>
        /// <param name="id">Mã phòng.</param>
        /// <returns>Chuyển hướng kèm thông báo.</returns>
        public async Task<IActionResult> DeleteRoom(int? id)
        {
            if (id == null) return NotFound();
            var room = await _context.Rooms
                .Include(r => r.Showtimes)
                .FirstOrDefaultAsync(r => r.Id == id);
            if (room == null) return NotFound();

            int currentCinemaId = room.CinemaId;

            if (room.Showtimes != null && room.Showtimes.Any())
            {
                TempData["ErrorMessage"] = $"Không thể xóa '{room.Name}' vì phòng này đang có {room.Showtimes.Count} suất chiếu.";
                return RedirectToAction(nameof(RoomList), new { cinemaId = currentCinemaId });
            }

            var seats = _context.Seats.Where(s => s.RoomId == id);
            _context.Seats.RemoveRange(seats);

            _context.Rooms.Remove(room);
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = $"Đã xóa {room.Name} thành công!";
            return RedirectToAction(nameof(RoomList), new { cinemaId = currentCinemaId });
        }

        // =================================================================
        // ======================= 4. MODULE QUẢN LÝ SUẤT CHIẾU ============
        // =================================================================

        /// <summary>
        /// Chức năng: Hiển thị trang quản lý suất chiếu, hỗ trợ tìm kiếm theo tên phim.
        /// </summary>
        /// <param name="searchString">Từ khóa tìm kiếm.</param>
        /// <returns>View danh sách phim có suất chiếu.</returns>
        [HttpGet]
        public async Task<IActionResult> Showtime(string searchString)
        {
            ViewData["CurrentFilter"] = searchString;
            ViewBag.AllMovies = await _context.Movies.OrderByDescending(m => m.Id).ToListAsync();
            ViewBag.AllCinemas = await _context.Cinemas.ToListAsync();

            var moviesQuery = _context.Movies
                .Include(m => m.Showtimes)
                .Where(m => m.Showtimes.Any())
                .AsQueryable();

            if (!string.IsNullOrEmpty(searchString))
                moviesQuery = moviesQuery.Where(m => m.Title.Contains(searchString));

            var movies = await moviesQuery.OrderByDescending(m => m.Id).ToListAsync();
            return View(movies);
        }

        /// <summary>
        /// Chức năng: Xử lý thêm suất chiếu mới. Validate giờ chiếu và kiểm tra xung đột lịch chiếu.
        /// </summary>
        /// <param name="showtime">Dữ liệu suất chiếu.</param>
        /// <returns>Chuyển hướng kèm thông báo (Thành công hoặc Lỗi xung đột).</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateShowtime(Showtime showtime)
        {
            ModelState.Remove("Movie");
            ModelState.Remove("Room");
            ModelState.Remove("Cinema");
            ModelState.Remove("BookingSeats");
            ModelState.Remove("Status");

            if (ModelState.IsValid)
            {
                if (showtime.EndTime <= showtime.StartTime)
                {
                    TempData["ErrorMessage"] = "Giờ kết thúc phải sau giờ bắt đầu!";
                    return RedirectToAction(nameof(Showtime));
                }

                int cleaningMinutes = 15;
                bool isConflict = await _context.Showtimes.AnyAsync(s =>
                    s.RoomId == showtime.RoomId &&
                    showtime.StartTime < s.EndTime.AddMinutes(cleaningMinutes) &&
                    showtime.EndTime.AddMinutes(cleaningMinutes) > s.StartTime
                );

                if (isConflict)
                {
                    TempData["ErrorMessage"] = $"Lỗi Xung Đột: Phòng này đã có lịch chiếu khác (đã tính {cleaningMinutes}p dọn dẹp)!";
                    return RedirectToAction(nameof(Showtime));
                }

                showtime.Status = "scheduled";
                _context.Showtimes.Add(showtime);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Đã xếp lịch chiếu thành công!";
            }
            return RedirectToAction(nameof(Showtime));
        }

        /// <summary>
        /// Chức năng: API phục vụ AJAX lấy danh sách phòng khi chọn Rạp.
        /// </summary>
        /// <param name="cinemaId">Mã rạp.</param>
        /// <returns>JSON danh sách phòng.</returns>
        [HttpGet]
        public async Task<IActionResult> GetRoomsByCinema(int cinemaId)
        {
            var rooms = await _context.Rooms
                .Where(r => r.CinemaId == cinemaId && r.Status == 1)
                .Select(r => new { id = r.Id, name = r.Name })
                .ToListAsync();
            return Json(rooms);
        }

        /// <summary>
        /// Chức năng: API phục vụ AJAX lấy danh sách suất chiếu bận của phòng (giúp Admin tránh xếp trùng giờ).
        /// </summary>
        /// <param name="roomId">Mã phòng.</param>
        /// <param name="date">Ngày cần kiểm tra.</param>
        /// <returns>JSON danh sách lịch bận.</returns>
        [HttpGet]
        public async Task<IActionResult> GetBusySchedules(int roomId, string? date)
        {
            var query = _context.Showtimes
                .Include(s => s.Movie)
                .Where(s => s.RoomId == roomId);

            if (!string.IsNullOrEmpty(date) && DateTime.TryParse(date, out DateTime selectedDate))
                query = query.Where(s => s.StartTime.Date == selectedDate.Date);
            else
                query = query.Where(s => s.StartTime.Date >= DateTime.Today);

            var schedules = await query
                .OrderBy(s => s.StartTime)
                .Select(s => new {
                    title = s.Movie.Title,
                    date = s.StartTime.ToString("dd/MM/yyyy"),
                    start = s.StartTime.ToString("HH:mm"),
                    end = s.EndTime.AddMinutes(15).ToString("HH:mm")
                }).ToListAsync();

            return Json(schedules);
        }

        /// <summary>
        /// Chức năng: Hiển thị danh sách chi tiết các suất chiếu của một phim cụ thể.
        /// </summary>
        /// <param name="movieId">Mã phim.</param>
        /// <returns>View danh sách chi tiết.</returns>
        [HttpGet]
        public async Task<IActionResult> ShowtimeList(int? movieId)
        {
            if (movieId == null) return NotFound();
            var movie = await _context.Movies.FindAsync(movieId);
            if (movie == null) return NotFound();

            var showtimes = await _context.Showtimes
                .Include(s => s.Cinema)
                .Include(s => s.Room)
                .Include(s => s.BookingSeats)
                .Where(s => s.MovieId == movieId)
                .OrderBy(s => s.StartTime)
                .ToListAsync();

            ViewBag.MovieInfo = movie;
            return View(showtimes);
        }

        /// <summary>
        /// Chức năng: Hủy 1 suất chiếu. Chặn hủy nếu suất chiếu đã có khách đặt vé.
        /// </summary>
        /// <param name="id">Mã suất chiếu.</param>
        /// <returns>Chuyển hướng kèm thông báo.</returns>
        public async Task<IActionResult> DeleteShowtime(int? id)
        {
            if (id == null) return NotFound();
            var showtime = await _context.Showtimes
                .Include(s => s.BookingSeats)
                .FirstOrDefaultAsync(s => s.Id == id);
            if (showtime == null) return NotFound();

            int currentMovieId = showtime.MovieId;

            bool hasBookings = showtime.BookingSeats != null &&
                               showtime.BookingSeats.Any(bs => bs.Status == "confirmed");
            if (hasBookings)
            {
                TempData["ErrorMessage"] = "Không thể hủy suất chiếu này vì đã có khách hàng đặt vé!";
                return RedirectToAction(nameof(ShowtimeList), new { movieId = currentMovieId });
            }

            _context.Showtimes.Remove(showtime);
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Đã hủy suất chiếu thành công!";
            return RedirectToAction(nameof(ShowtimeList), new { movieId = currentMovieId });
        }

        /// <summary>
        /// Chức năng: Hủy toàn bộ lịch chiếu của một phim. Chặn hủy nếu có bất kỳ suất nào đã bán vé.
        /// </summary>
        /// <param name="movieId">Mã phim.</param>
        /// <returns>Chuyển hướng kèm thông báo.</returns>
        public async Task<IActionResult> DeleteAllShowtimes(int? movieId)
        {
            if (movieId == null) return NotFound();
            var movie = await _context.Movies.FindAsync(movieId);
            if (movie == null) return NotFound();

            var showtimes = await _context.Showtimes
                .Include(s => s.BookingSeats)
                .Where(s => s.MovieId == movieId)
                .ToListAsync();

            if (showtimes.Any())
            {
                bool anyHasBooking = showtimes.Any(s =>
                    s.BookingSeats != null && s.BookingSeats.Any(bs => bs.Status == "confirmed"));

                if (anyHasBooking)
                {
                    TempData["ErrorMessage"] = $"Không thể hủy toàn bộ lịch chiếu vì có suất chiếu đã bán vé. Vui lòng hủy từng suất!";
                    return RedirectToAction(nameof(Showtime));
                }

                _context.Showtimes.RemoveRange(showtimes);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = $"Đã hủy toàn bộ lịch công chiếu của phim '{movie.Title}'.";
            }
            else
            {
                TempData["ErrorMessage"] = "Phim này hiện chưa có lịch chiếu nào để xóa.";
            }
            return RedirectToAction(nameof(Showtime));
        }

        // =================================================================
        // ======================= 5. MODULE BÁO CÁO DOANH THU =============
        // =================================================================

        /// <summary>
        /// Chức năng: Thống kê doanh thu theo Phim và theo Ngày dựa trên tháng được chọn. 
        /// Đóng gói JSON phục vụ vẽ biểu đồ.
        /// </summary>
        /// <param name="selectedMonth">Tháng lựa chọn (Định dạng "YYYY-MM").</param>
        /// <returns>View chứa các ViewBag dữ liệu thống kê.</returns>
        [HttpGet]
        public async Task<IActionResult> RevenueReport(string selectedMonth)
        {
            int month = DateTime.Now.Month;
            int year = DateTime.Now.Year;

            if (!string.IsNullOrEmpty(selectedMonth))
            {
                var parts = selectedMonth.Split('-');
                if (parts.Length == 2)
                {
                    int.TryParse(parts[0], out year);
                    int.TryParse(parts[1], out month);
                }
            }
            else
            {
                selectedMonth = $"{year}-{month:D2}";
            }

            ViewBag.SelectedMonth = selectedMonth;

            var bookings = await _context.Bookings
                .Include(b => b.Showtime).ThenInclude(s => s.Movie)
                .Include(b => b.BookingSeats)
                .Where(b => b.BookingTime.Month == month &&
                            b.BookingTime.Year == year &&
                            b.Status == "confirmed")
                .ToListAsync();

            var movieRevenue = bookings
                .GroupBy(b => b.Showtime.Movie.Title)
                .Select(g => new MovieRevVM
                {
                    MovieName = g.Key,
                    TicketsSold = g.Sum(b => b.BookingSeats.Count(bs => bs.Status == "confirmed")),
                    Revenue = g.Sum(b => b.TotalPrice)
                })
                .OrderByDescending(m => m.Revenue)
                .ToList();

            var dailyRevenue = bookings
                .GroupBy(b => b.BookingTime.Date)
                .Select(g => new DailyRevVM
                {
                    DateString = g.Key.ToString("dd/MM/yyyy"),
                    Revenue = g.Sum(b => b.TotalPrice)
                })
                .OrderBy(x => x.DateString)
                .ToList();

            ViewBag.MovieLabels = JsonSerializer.Serialize(movieRevenue.Select(m => m.MovieName));
            ViewBag.MovieData = JsonSerializer.Serialize(movieRevenue.Select(m => m.Revenue));
            ViewBag.DateLabels = JsonSerializer.Serialize(dailyRevenue.Select(d => d.DateString));
            ViewBag.DateData = JsonSerializer.Serialize(dailyRevenue.Select(d => d.Revenue));
            ViewBag.MovieTable = movieRevenue;
            ViewBag.DateTable = dailyRevenue;
            ViewBag.TotalRevenue = bookings.Sum(b => b.TotalPrice);

            return View();
        }
    }

    // ==========================================
    // CÁC LỚP PHỤ TRỢ (ViewModels)
    // ==========================================

    /// <summary>
    /// Chức năng: Chứa cấu trúc dữ liệu trả về cho bảng thống kê doanh thu theo Phim.
    /// </summary>
    public class MovieRevVM
    {
        public string MovieName { get; set; }
        public int TicketsSold { get; set; }
        public double Revenue { get; set; }
    }

    /// <summary>
    /// Chức năng: Chứa cấu trúc dữ liệu trả về cho bảng thống kê doanh thu theo Ngày.
    /// </summary>
    public class DailyRevVM
    {
        public string DateString { get; set; }
        public double Revenue { get; set; }
    }
}