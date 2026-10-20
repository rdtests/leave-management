using LeaveManagement.Api.Controllers;
using LeaveManagement.Api.Data;
using LeaveManagement.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace LeaveManagement.Tests;

public class LeaveRequestsTests : IClassFixture<TestDatabase>
{
    private const string NotEnoughBalance = "Not enough vacation balance";

    private readonly TestDatabase _database;

    public LeaveRequestsTests(TestDatabase database)
    {
        _database = database;
    }

    [Fact]
    public void Create_WithinQuota_Succeeds()
    {
        // Arrange
        using var db = _database.NewDb();
        var emp = new Employee { Name = "Test Emp", AnnualQuota = 20 };
        db.Employees.Add(emp);
        db.SaveChanges();

        var controller = new LeaveRequestsController(db);

        // Act: request 3 days, well within the quota.
        var result = controller.Create(new CreateLeaveRequestDto
        {
            EmployeeId = emp.Id,
            Type = LeaveType.Vacation,
            StartDate = new DateTime(2026, 3, 1),
            EndDate = new DateTime(2026, 3, 3)
        });

        // Assert
        Assert.IsType<OkObjectResult>(result);
        Assert.Single(db.LeaveRequests);
    }

    [Fact]
    public void Create_SingleRequestOverQuota_IsRejected()
    {
        // Arrange
        using var db = _database.NewDb();
        var emp = AddEmployee(db, quota: 20);
        var controller = new LeaveRequestsController(db);

        // Act: 21 days against a quota of 20.
        var result = controller.Create(Dto(emp, new DateTime(2026, 3, 1), new DateTime(2026, 3, 21)));

        // Assert
        AssertBadRequest(NotEnoughBalance, result);
        Assert.Empty(db.LeaveRequests);
    }

    [Fact]
    public void Create_PushesOverQuotaAfterUsedDays_IsRejected()
    {
        // Arrange
        using var db = _database.NewDb();
        var emp = AddEmployee(db, quota: 20);
        AddRequest(db, emp, new DateTime(2026, 1, 1), 18, LeaveStatus.Approved);
        var controller = new LeaveRequestsController(db);

        // Act: 18 used + 3 requested = 21 > 20, although 3 alone is within the quota.
        var result = controller.Create(Dto(emp, new DateTime(2026, 3, 1), new DateTime(2026, 3, 3)));

        // Assert
        AssertBadRequest(NotEnoughBalance, result);
        Assert.Single(db.LeaveRequests);
    }

    [Fact]
    public void Create_ExactlyUpToQuota_Succeeds()
    {
        // Arrange
        using var db = _database.NewDb();
        var emp = AddEmployee(db, quota: 20);
        AddRequest(db, emp, new DateTime(2026, 1, 1), 18, LeaveStatus.Approved);
        var controller = new LeaveRequestsController(db);

        // Act: 18 used + 2 requested = 20, right at the limit.
        var result = controller.Create(Dto(emp, new DateTime(2026, 3, 1), new DateTime(2026, 3, 2)));

        // Assert
        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(2, db.LeaveRequests.Count());
    }

    [Fact]
    public void Create_PendingRequestsCountAgainstQuota()
    {
        // Arrange
        using var db = _database.NewDb();
        var emp = AddEmployee(db, quota: 20);
        AddRequest(db, emp, new DateTime(2026, 1, 1), 18, LeaveStatus.Pending);
        var controller = new LeaveRequestsController(db);

        // Act
        var result = controller.Create(Dto(emp, new DateTime(2026, 3, 1), new DateTime(2026, 3, 3)));

        // Assert
        AssertBadRequest(NotEnoughBalance, result);
    }

    [Fact]
    public void Create_RejectedRequestsDoNotCountAgainstQuota()
    {
        // Arrange
        using var db = _database.NewDb();
        var emp = AddEmployee(db, quota: 20);
        AddRequest(db, emp, new DateTime(2026, 1, 1), 18, LeaveStatus.Rejected);
        var controller = new LeaveRequestsController(db);

        // Act
        var result = controller.Create(Dto(emp, new DateTime(2026, 3, 1), new DateTime(2026, 3, 3)));

        // Assert
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public void Create_DaysFromPreviousYearAreNotCounted()
    {
        // Arrange
        using var db = _database.NewDb();
        var emp = AddEmployee(db, quota: 20);
        AddRequest(db, emp, new DateTime(2025, 6, 1), 18, LeaveStatus.Approved);
        var controller = new LeaveRequestsController(db);

        // Act
        var result = controller.Create(Dto(emp, new DateTime(2026, 3, 1), new DateTime(2026, 3, 3)));

        // Assert
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public void Create_YearBoundary_OnlyCountsDaysInTheRequestYear()
    {
        // Arrange: 18 days ending on Dec 31, 2025 and 18 days starting on Jan 1, 2027.
        using var db = _database.NewDb();
        var emp = AddEmployee(db, quota: 20);
        AddRequest(db, emp, new DateTime(2025, 12, 14), 18, LeaveStatus.Approved);
        AddRequest(db, emp, new DateTime(2027, 1, 1), 18, LeaveStatus.Approved);
        var controller = new LeaveRequestsController(db);

        // Act: neither neighbouring year reduces the 2026 balance.
        var result = controller.Create(Dto(emp, new DateTime(2026, 1, 1), new DateTime(2026, 1, 20)));

        // Assert
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public void Create_PendingAndApprovedTogether_CountAgainstQuota()
    {
        // Arrange: 10 approved + 8 pending = 18 used, 2 left.
        using var db = _database.NewDb();
        var emp = AddEmployee(db, quota: 20);
        AddRequest(db, emp, new DateTime(2026, 1, 1), 10, LeaveStatus.Approved);
        AddRequest(db, emp, new DateTime(2026, 2, 1), 8, LeaveStatus.Pending);
        var controller = new LeaveRequestsController(db);

        // Act + Assert: 3 days is over the remaining 2, 2 days fits exactly.
        AssertBadRequest(NotEnoughBalance,
            controller.Create(Dto(emp, new DateTime(2026, 3, 1), new DateTime(2026, 3, 3))));
        Assert.IsType<OkObjectResult>(
            controller.Create(Dto(emp, new DateTime(2026, 3, 1), new DateTime(2026, 3, 2))));
    }

    [Fact]
    public void Create_DaysOfOtherEmployeesAreNotCounted()
    {
        // Arrange
        using var db = _database.NewDb();
        var emp = AddEmployee(db, quota: 20);
        var other = AddEmployee(db, quota: 20);
        AddRequest(db, other, new DateTime(2026, 1, 1), 18, LeaveStatus.Approved);
        var controller = new LeaveRequestsController(db);

        // Act
        var result = controller.Create(Dto(emp, new DateTime(2026, 3, 1), new DateTime(2026, 3, 3)));

        // Assert
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public void Create_SickDaysUsedDoNotReduceVacationBalance()
    {
        // Arrange
        using var db = _database.NewDb();
        var emp = AddEmployee(db, quota: 20);
        AddRequest(db, emp, new DateTime(2026, 1, 1), 18, LeaveStatus.Approved, LeaveType.Sick);
        var controller = new LeaveRequestsController(db);

        // Act
        var result = controller.Create(Dto(emp, new DateTime(2026, 3, 1), new DateTime(2026, 3, 3)));

        // Assert
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public void Create_SickRequestIsNotCheckedAgainstVacationQuota()
    {
        // Arrange
        using var db = _database.NewDb();
        var emp = AddEmployee(db, quota: 20);
        var controller = new LeaveRequestsController(db);

        // Act: 30 sick days, more than the vacation quota.
        var result = controller.Create(Dto(emp, new DateTime(2026, 4, 1), new DateTime(2026, 4, 30), LeaveType.Sick));

        // Assert
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public void Create_EndBeforeStart_IsRejected()
    {
        // Arrange
        using var db = _database.NewDb();
        var emp = AddEmployee(db, quota: 20);
        var controller = new LeaveRequestsController(db);

        // Act
        var result = controller.Create(Dto(emp, new DateTime(2026, 3, 5), new DateTime(2026, 3, 1)));

        // Assert
        AssertBadRequest("End date cannot be before start date", result);
        Assert.Empty(db.LeaveRequests);
    }

    [Fact]
    public void Create_SpanningTwoYears_IsRejected()
    {
        // Arrange
        using var db = _database.NewDb();
        var emp = AddEmployee(db, quota: 20);
        var controller = new LeaveRequestsController(db);

        // Act: 4 days, within the quota, so only the year check can reject it.
        var result = controller.Create(Dto(emp, new DateTime(2026, 12, 30), new DateTime(2027, 1, 2)));

        // Assert
        AssertBadRequest("A request cannot span more than one calendar year", result);
        Assert.Empty(db.LeaveRequests);
    }

    private static Employee AddEmployee(LeaveDbContext db, int quota)
    {
        var emp = new Employee { Name = "Test Emp", AnnualQuota = quota };
        db.Employees.Add(emp);
        db.SaveChanges();
        return emp;
    }

    private static void AddRequest(
        LeaveDbContext db, Employee emp, DateTime start, int days, LeaveStatus status,
        LeaveType type = LeaveType.Vacation)
    {
        db.LeaveRequests.Add(new LeaveRequest
        {
            EmployeeId = emp.Id,
            Type = type,
            StartDate = start,
            EndDate = start.AddDays(days - 1),
            Days = days,
            Status = status
        });
        db.SaveChanges();
    }

    private static CreateLeaveRequestDto Dto(
        Employee emp, DateTime start, DateTime end, LeaveType type = LeaveType.Vacation) => new()
    {
        EmployeeId = emp.Id,
        Type = type,
        StartDate = start,
        EndDate = end
    };

    private static void AssertBadRequest(string expectedMessage, IActionResult result)
    {
        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(expectedMessage, badRequest.Value);
    }

    
}
