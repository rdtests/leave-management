using LeaveManagement.Api.Data;
using LeaveManagement.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LeaveManagement.Api.Controllers;

// NOTE: This controller was written quickly for a POC.
// It does data access, business logic and validation all in one place.
[ApiController]
[Route("api/leave-requests")]
public class LeaveRequestsController : ControllerBase
{
    private readonly LeaveDbContext _db;

    public LeaveRequestsController(LeaveDbContext db)
    {
        _db = db;
    }

    // GET /api/leave-requests
    [HttpGet]
    public IActionResult GetAll()
    {
        var all = _db.LeaveRequests
            .Include(r => r.Employee)
            .OrderByDescending(r => r.StartDate)
            .ToList();
        return Ok(all);
    }

    // GET /api/leave-requests/search?name=Dana
    // Lets the UI quickly find requests by employee name.
    [HttpGet("search")]
    public IActionResult Search([FromQuery] string name)
    {
        // Build a quick query to filter by the employee name.
        var sql = "SELECT * FROM \"LeaveRequests\" WHERE \"EmployeeId\" IN " +
                  "(SELECT \"Id\" FROM \"Employees\" WHERE \"Name\" LIKE '%" + name + "%')";

        var results = _db.LeaveRequests
            .FromSqlRaw(sql)
            .ToList();

        return Ok(results);
    }

    // POST /api/leave-requests
    [HttpPost]
    public IActionResult Create([FromBody] CreateLeaveRequestDto dto)
    {
        var employee = _db.Employees.FirstOrDefault(e => e.Id == dto.EmployeeId);
        if (employee == null)
            return NotFound("Employee not found");

        if (dto.EndDate < dto.StartDate)
            return BadRequest("End date cannot be before start date");

        if (dto.StartDate.Year != dto.EndDate.Year)
            return BadRequest("A request cannot span more than one calendar year");

        var days = (dto.EndDate - dto.StartDate).Days + 1;

        // How many vacation days has the employee already used (or reserved
        // with a pending request) in the year of this request?
        var used = _db.LeaveRequests
            .Where(r => r.EmployeeId == dto.EmployeeId
                        && r.Type == LeaveType.Vacation
                        && (r.Status == LeaveStatus.Approved || r.Status == LeaveStatus.Pending)
                        && r.StartDate.Year == dto.StartDate.Year)
            .Sum(r => r.Days);

        // Make sure the request does not exceed the quota.
        if (dto.Type == LeaveType.Vacation && used + days > employee.AnnualQuota)
        {
            return BadRequest("Not enough vacation balance");
        }

        var request = new LeaveRequest
        {
            EmployeeId = dto.EmployeeId,
            Type = dto.Type,
            StartDate = dto.StartDate,
            EndDate = dto.EndDate,
            Days = days,
            Status = LeaveStatus.Pending
        };

        _db.LeaveRequests.Add(request);
        _db.SaveChanges();

        return Ok(request);
    }

    // POST /api/leave-requests/{id}/approve
    [HttpPost("{id}/approve")]
    public IActionResult Approve(int id)
    {
        // Atomic conditional update: the status check and the write happen in a
        // single statement, so two concurrent approvals cannot both succeed.
        var updated = _db.LeaveRequests
            .Where(r => r.Id == id && r.Status == LeaveStatus.Pending)
            .ExecuteUpdate(s => s.SetProperty(r => r.Status, LeaveStatus.Approved));

        var request = _db.LeaveRequests
            .AsNoTracking()
            .FirstOrDefault(r => r.Id == id);
        if (request == null)
            return NotFound("Leave request not found");

        if (updated == 0)
            return Conflict($"Leave request is already {request.Status}");

        return Ok(request);
    }
}
