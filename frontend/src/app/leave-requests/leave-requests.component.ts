import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import {
  AbstractControl,
  FormControl,
  FormGroup,
  ReactiveFormsModule,
  ValidationErrors,
  Validators
} from '@angular/forms';
import { CreateLeaveRequest, Employee, LeaveType } from '../models/leave-request.model';

const MS_PER_DAY = 24 * 60 * 60 * 1000;

// Inclusive calendar days between two yyyy-MM-dd values, same as the backend.
function inclusiveDays(start: string, end: string): number {
  return Math.round((Date.parse(end) - Date.parse(start)) / MS_PER_DAY) + 1;
}

// Converts a dd/MM/yyyy string (the format shown in the table) to yyyy-MM-dd,
// or null when it is not a real calendar date.
function toIsoDate(value: string | null | undefined): string | null {
  const match = /^(\d{2})\/(\d{2})\/(\d{4})$/.exec((value ?? '').trim());
  if (!match) return null;
  const [, dd, mm, yyyy] = match;
  const date = new Date(Date.UTC(+yyyy, +mm - 1, +dd));
  const valid =
    date.getUTCFullYear() === +yyyy && date.getUTCMonth() === +mm - 1 && date.getUTCDate() === +dd;
  return valid ? `${yyyy}-${mm}-${dd}` : null;
}

// Empty is left to `required`; anything else must be a valid dd/MM/yyyy date.
function dateFormatValidator(control: AbstractControl): ValidationErrors | null {
  if (!control.value) return null;
  return toIsoDate(control.value) ? null : { dateFormat: true };
}

// Start date must not be later than the end date (which also rules out negative days).
function dateRangeValidator(group: AbstractControl): ValidationErrors | null {
  const start = toIsoDate(group.get('startDate')?.value);
  const end = toIsoDate(group.get('endDate')?.value);
  if (!start || !end) return null;
  return inclusiveDays(start, end) < 1 ? { dateRange: true } : null;
}

// NOTE: This component was written quickly for a POC.
// It talks to the API directly, manages state by hand and uses `any` everywhere.
@Component({
  selector: 'app-leave-requests',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  templateUrl: './leave-requests.component.html',
  styleUrls: ['./leave-requests.component.css']
})
export class LeaveRequestsComponent implements OnInit {
  requests: any[] = [];
  loading = false;

  employees: Employee[] = [];
  submitting = false;
  submitted = false;
  serverError = '';

  leaveTypes = [
    { value: LeaveType.Vacation, label: 'Vacation' },
    { value: LeaveType.Sick, label: 'Sick' },
    { value: LeaveType.Unpaid, label: 'Unpaid' }
  ];

  form = new FormGroup(
    {
      employeeId: new FormControl<number | null>(null, Validators.required),
      type: new FormControl<LeaveType | null>(null, Validators.required),
      startDate: new FormControl('', {
        nonNullable: true,
        validators: [Validators.required, dateFormatValidator]
      }),
      endDate: new FormControl('', {
        nonNullable: true,
        validators: [Validators.required, dateFormatValidator]
      })
    },
    { validators: dateRangeValidator }
  );

  private apiUrl = 'http://localhost:5080/api/leave-requests';
  private employeesUrl = 'http://localhost:5080/api/employees';

  constructor(private http: HttpClient) {}

  ngOnInit(): void {
    this.load();
    this.http.get<Employee[]>(this.employeesUrl).subscribe((data) => {
      this.employees = data;
    });
  }

  // Calendar days the request covers, or null until both dates are valid.
  get days(): number | null {
    const start = toIsoDate(this.form.value.startDate);
    const end = toIsoDate(this.form.value.endDate);
    if (!start || !end) return null;
    const days = inclusiveDays(start, end);
    return days >= 1 ? days : null;
  }

  // The calendar popup gives yyyy-MM-dd; the text field shows dd/MM/yyyy.
  pickDate(name: 'startDate' | 'endDate', iso: string): void {
    if (!iso) return;
    const [yyyy, mm, dd] = iso.split('-');
    const control = this.form.controls[name];
    control.setValue(`${dd}/${mm}/${yyyy}`);
    control.markAsTouched();
  }

  // Show an error once the field was touched or a submit was attempted.
  showError(name: string): boolean {
    const control = this.form.get(name);
    return !!control && control.invalid && (control.touched || this.submitted);
  }

  showRangeError(): boolean {
    return this.form.hasError('dateRange') && (this.submitted || this.form.get('endDate')!.touched);
  }

  submit(): void {
    this.submitted = true;
    this.serverError = '';
    if (this.form.invalid) return;

    const raw = this.form.getRawValue();
    const payload: CreateLeaveRequest = {
      employeeId: raw.employeeId!,
      type: raw.type!,
      startDate: toIsoDate(raw.startDate)!,
      endDate: toIsoDate(raw.endDate)!
    };
    this.submitting = true;
    this.http.post(this.apiUrl, payload).subscribe({
      next: () => {
        this.submitting = false;
        this.submitted = false;
        this.form.reset();
        this.load();
      },
      error: (err: HttpErrorResponse) => {
        this.submitting = false;
        this.serverError =
          typeof err.error === 'string' && err.error
            ? err.error
            : 'Could not submit the request. Please try again.';
      }
    });
  }

  load(): void {
    this.loading = true;
    this.http.get<any>(this.apiUrl).subscribe((data) => {
      this.requests = data;
      this.loading = false;
    });
  }

  // Wired up by the candidate as part of the assignment.
  approve(id: number): void {
    // TODO (candidate): call POST /api/leave-requests/{id}/approve
    // and handle loading / error / success without a generic alert.
    this.http.post<any>(this.apiUrl + '/' + id + '/approve', {}).subscribe(() => {
      this.load();
    });
  }

  typeLabel(type: number): string {
    if (type == 0) return 'Vacation';
    if (type == 1) return 'Sick';
    return 'Unpaid';
  }

  statusLabel(status: number): string {
    if (status == 0) return 'Pending';
    if (status == 1) return 'Approved';
    return 'Rejected';
  }
}
