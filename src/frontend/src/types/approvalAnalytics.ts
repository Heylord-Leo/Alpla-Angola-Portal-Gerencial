// v2.244.0 Approval Center V2 — Phase 3. Read-only analytics shapes (mirror backend ApprovalAnalyticsDto).
// Durations are seconds; the frontend formats them (never recomputes a metric).

export interface DurationStats {
  sampleCount: number;
  averageSeconds: number;
  medianSeconds: number;
  p90Seconds: number;
  minSeconds: number;
  maxSeconds: number;
}

export interface ApprovalAnalyticsSummary {
  totalDecisions: number;
  approved: number;
  rejected: number;
  returned: number;
  resubmitted: number;
  approvalRate: number;
  rejectionRate: number;
  returnRate: number;
}

export interface ApprovalBottleneck {
  areaAverageSeconds: number;
  finalAverageSeconds: number;
  areaSampleCount: number;
  finalSampleCount: number;
  dominantStage: 'AREA' | 'FINAL' | null;
}

export interface ApproverStats {
  approverUserId: string;
  approverName: string;
  decisionCount: number;
  approved: number;
  rejected: number;
  returned: number;
  speedSampleCount: number;
  averageDecisionSeconds: number | null;
  medianDecisionSeconds: number | null;
  p90DecisionSeconds: number | null;
}

export interface ApprovalTrendPoint {
  bucket: string;
  decisions: number;
  approved: number;
  rejected: number;
  returned: number;
  avgDurationSeconds: number | null;
}

export interface ApprovalAnalytics {
  dateFrom: string | null;
  dateTo: string | null;
  resolution: string;
  summary: ApprovalAnalyticsSummary;
  duration: DurationStats;
  areaDuration: DurationStats;
  finalDuration: DurationStats;
  bottleneck: ApprovalBottleneck;
  approvers: ApproverStats[];
  trend: ApprovalTrendPoint[];
  requestTypes: { quotation: number; payment: number };
}

export interface ApprovalAnalyticsQuery {
  dateFrom?: string;
  dateTo?: string;
  resolution?: 'day' | 'week' | 'month';
  requestType?: string;
  departmentId?: number;
  companyId?: number;
  plantId?: number;
  approverId?: string;
}
