// Intentionally thin. Part of the task is to introduce proper typing
// across the frontend instead of the `any` usage in the component.

export interface Employee {
  id: number;
  name: string;
  annualQuota: number;
}

// Mirrors the backend LeaveType enum.
export enum LeaveType {
  Vacation = 0,
  Sick = 1,
  Unpaid = 2
}

// Payload for POST /api/leave-requests (mirrors CreateLeaveRequestDto).
export interface CreateLeaveRequest {
  employeeId: number;
  type: LeaveType;
  startDate: string;
  endDate: string;
}

export interface LeaveRequest {
  id: number;
  employeeId: number;
  // Populated by the API on reads; absent on the response to a create.
  employee?: Employee;
  type: number;
  startDate: string;
  endDate: string;
  status: number;
  days: number;
}
