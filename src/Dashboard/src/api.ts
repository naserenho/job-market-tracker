import { apiQuery, type Filters } from './filters';

export type SkillLevel = 'None' | 'Learning' | 'Working' | 'Strong';
export type TextKind = 'full' | 'snippet' | 'all';
export type PostingSort = 'newest' | 'match' | 'salary';

// The active job market, from config/regions/<name>.json: names, currency, and how numbers and dates are written.
export interface MetaRegion {
  name: string;
  title: string;
  localLabel: string;
  remoteLabel: string;
  currency: string;
  moneyPrefix: string;
  culture: string;
  timeZone: string;
}

export interface Meta {
  latestRunId: string | null;
  latestRunAt: string | null;
  runs: number;
  historyDays: number;
  targetFamilies: string[];
  families: string[];
  seniorities: string[];
  categories: string[];
  companies: string[];
  stale: boolean;
  region: MetaRegion;
}

export interface PostingRow {
  id: string;
  title: string;
  company: string;
  location: string;
  url: string;
  salaryMin: number | null;
  salaryMax: number | null;
  salaryPeriod: 'Year' | 'Hour' | 'Month' | null;
  match: number;
  firstSeen: string;
  skills: string[];
  citizenshipOrClearance: boolean;
  source: string;
  isFullText: boolean;
}

export interface PostingPage { total: number; page: number; pageSize: number; items: PostingRow[] }

export interface SkillRow {
  skill: string;
  category: string;
  share: number;
  count: number;
  level: SkillLevel;
  previousShare: number | null;
  change: number | null;
}

export interface SkillList { postings: number; skills: SkillRow[] }

export interface NamedCount { name: string; count: number }

export interface SkillDetail {
  skill: string;
  category: string;
  level: SkillLevel;
  share: number;
  count: number;
  postings: number;
  salary: { withMedian: number | null; withCount: number; withoutMedian: number | null; withoutCount: number };
  coSkills: NamedCount[];
  companies: NamedCount[];
  examples: PostingRow[];
}

export interface SalaryCell {
  family: string;
  seniority: string;
  count: number;
  median: number | null;
  p25: number | null;
  p75: number | null;
}

export interface Bucket { from: number; to: number; count: number }

export interface SalaryView { postings: number; withSalary: number; cells: SalaryCell[]; histogram: Bucket[]; examples: PostingRow[] }

export interface SalaryRange { min: number; max: number; period: 'Year' | 'Hour' | 'Month' }

export interface ReportModel {
  generatedAt: string;
  latestRunId: string;
  latestRunAt: string;
  windowDays: number;
  snapshot: {
    activeNow: number;
    newSinceLastRun: number;
    inWindow: number;
    fullText: number;
    snippets: number;
    withSalary: number;
    citizenshipFlagged: number;
    runs: number;
    historyDays: number;
    bySource: Record<string, number>;
  };
  topSkills: { skill: string; category: string; fullTextShare: number; snippetShare: number; count: number; level: SkillLevel }[];
  gaps: SkillScore[];
  strengths: SkillScore[];
  salaries: SalaryCell[];
  companies: { company: string; postings: number }[];
  matches: {
    title: string;
    company: string;
    url: string;
    location: string;
    score: number;
    skillCount: number;
    missing: string[];
    salary: SalaryRange | null;
    citizenshipOrClearance: boolean;
    postingId: string;
  }[];
  trends: { weekly: { weekStart: string; active: number }[]; moves: { skill: string; previous: number; current: number }[] } | null;
  warnings: string[];
}

export interface SkillScore { skill: string; category: string; demand: number; level: SkillLevel; score: number }

export interface Overview { report: ReportModel | null }

export interface PostingParams { q?: string; skill?: string; company?: string; sort?: PostingSort; page?: number; pageSize?: number }

export class ApiError extends Error {
  readonly status: number;
  // The problem-details body (field, id, …) when the API sent one.
  readonly details: Record<string, unknown>;

  constructor(message: string, status: number, details: Record<string, unknown> = {}) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.details = details;
  }
}

export interface RequestOptions {
  method?: string;
  headers?: Record<string, string>;
  body?: BodyInit;
}

const UNREACHABLE = 'The API is not reachable. Is the dashboard (scripts/dashboard.cmd) running?';

export async function request<T>(path: string, options: RequestOptions = {}): Promise<T> {
  let response: Response;
  try {
    response = await fetch(path, { method: options.method, headers: { Accept: 'application/json', ...options.headers }, body: options.body });
  } catch {
    throw new ApiError(UNREACHABLE, 0);
  }
  if (!response.ok) {
    let message = '';
    let details: Record<string, unknown> = {};
    try {
      details = (await response.json()) as Record<string, unknown>;
      const text = typeof details.detail === 'string' ? details.detail : typeof details.title === 'string' ? details.title : '';
      if (text) message = text;
    } catch {
      // Not a problem-details body.
    }
    // The API always sends problem details. A bare 5xx comes from the dev proxy when the API itself is down.
    if (!message) message = response.status >= 500 ? UNREACHABLE : `The API answered ${response.status}.`;
    throw new ApiError(message, response.status, details);
  }
  if (response.status === 204) return undefined as T;
  return (await response.json()) as T;
}

function get<T>(path: string): Promise<T> {
  return request<T>(path);
}

export const api = {
  meta: () => get<Meta>('/api/meta'),
  overview: (f: Filters) => get<Overview>(`/api/overview${apiQuery(f)}`),
  skills: (f: Filters, text: TextKind, category?: string) => get<SkillList>(`/api/skills${apiQuery(f, { text, category })}`),
  skill: (name: string, f: Filters, text: TextKind) =>
    get<SkillDetail>(`/api/skills/${encodeURIComponent(name)}${apiQuery(f, { text })}`),
  salaries: (f: Filters, skill?: string, company?: string) => get<SalaryView>(`/api/salaries${apiQuery(f, { skill, company })}`),
  postings: (f: Filters, p: PostingParams) => get<PostingPage>(`/api/postings${apiQuery(f, { ...p })}`),
};
