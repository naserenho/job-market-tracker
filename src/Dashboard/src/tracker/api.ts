import { request } from '../api';

export type AppStatus = 'Shortlisted' | 'Applied' | 'Screening' | 'Interviewing' | 'Offer' | 'Accepted' | 'Rejected' | 'Withdrawn';
export const STATUSES: AppStatus[] = ['Shortlisted', 'Applied', 'Screening', 'Interviewing', 'Offer', 'Accepted', 'Rejected', 'Withdrawn'];
export type WorkModel = 'Unknown' | 'Remote' | 'Hybrid' | 'Onsite';
export const WORK_MODELS: WorkModel[] = ['Unknown', 'Remote', 'Hybrid', 'Onsite'];
export type DocumentKind = 'Cv' | 'CoverLetter' | 'Other';
export const DOCUMENT_KINDS: { kind: DocumentKind; label: string }[] = [
  { kind: 'Cv', label: 'CV' },
  { kind: 'CoverLetter', label: 'Cover letter' },
  { kind: 'Other', label: 'Other' },
];
export type TrackerSort = 'next' | 'updated' | 'company';

export interface TrackerStatus {
  migrated: boolean;
  lastBackupAt: string | null;
  lastBackupError: string | null;
  counts: Partial<Record<AppStatus, number>>;
  overdue: number;
}
export interface NextAction { id: string; action: string; dueOn: string }
export interface ApplicationRow {
  id: string;
  title: string;
  company: string;
  status: AppStatus;
  next: NextAction | null;
  updatedAt: string;
  postingId: string | null;
  live: boolean;
}
export interface UpNextItem {
  followUpId: string;
  applicationId: string;
  title: string;
  company: string;
  action: string;
  dueOn: string;
  overdue: boolean;
}
export type TrackerIndex = Record<string, { id: string; status: AppStatus }>;
export interface CompanyOption { id: string; name: string }
export interface CompanyView {
  id: string;
  name: string;
  website: string;
  officeLocation: string;
  companySize: string;
  annualLeave: string;
  benefits: string;
  notes: string;
  version: number;
  applications: number;
}
export interface EventView { id: string; onDate: string; fromStatus: AppStatus | null; toStatus: AppStatus; note: string }
export interface FollowUpView { id: string; action: string; dueOn: string; doneOn: string | null }
export interface ContactView { id: string; name: string; role: string; email: string; phone: string; linkedIn: string; notes: string }
export interface DocumentView { id: string; kind: DocumentKind; label: string; fileName: string; sizeBytes: number; sentOn: string; generated: boolean }
export type MarketState = 'Counted' | 'Pending' | 'OutOfScope' | 'Duplicate';
export interface MarketFacts { match: number; live: boolean; firstSeen: string; lastSeen: string; skills: string[] }
export interface ApplicationDetail {
  id: string;
  title: string;
  url: string;
  location: string;
  status: AppStatus;
  origin: 'Market' | 'Pasted';
  foundVia: string;
  workModel: WorkModel;
  officeDays: string;
  teamSize: string;
  workingHours: string;
  salaryPosted: string;
  salaryAsked: string;
  salaryOffered: string;
  notes: string;
  postingId: string | null;
  postingText: string;
  market: MarketFacts | null;
  marketState: MarketState | null;
  company: CompanyView;
  events: EventView[];
  followUps: FollowUpView[];
  contacts: ContactView[];
  documents: DocumentView[];
  createdAt: string;
  updatedAt: string;
  version: number;
}
export interface NewEntry { id: string }

export type ProfileLevel = 'None' | 'Learning' | 'Working' | 'Strong';
export interface PostingSkill { skill: string; weight: number; level: ProfileLevel }
export interface ProposalBullet { id: string; text: string; skills: string[]; score: number; always: boolean }
export interface ProposalSection {
  id: string;
  kind: 'Role' | 'Project';
  title: string;
  company: string;
  location: string;
  dates: string;
  maxBullets: number;
  bullets: ProposalBullet[];
}
export interface ProposalSkillItem { text: string; skills: string[]; matches: boolean }
export interface ProposalSkillGroup { name: string; items: ProposalSkillItem[] }
export interface ProposalSummary { id: string; families: string[]; text: string }
export interface ProposalCertification { name: string; year: string; skills: string[] }
export interface CvChoices { headline: string; summary: string; bulletIds: string[] }
export interface SavedChoices extends CvChoices { generatedOn: string; droppedIds: string[] }
export interface CvProposal {
  entryId: string;
  title: string;
  company: string;
  textSource: 'entry' | 'market' | 'none';
  thin: boolean;
  version: number;
  postingSkills: PostingSkill[];
  headline: string;
  summaries: ProposalSummary[];
  suggestedSummaryId: string;
  skillGroups: ProposalSkillGroup[];
  sections: ProposalSection[];
  certifications: ProposalCertification[];
  suggestion: CvChoices;
  saved: SavedChoices | null;
  fixedWords: number;
}
export interface CvStatus { ready: boolean; problems: string[] }
export interface GeneratedCv { documentId: string; fileName: string }

export interface PasteInput { title: string; company: string; url: string; location: string; postingText: string; status: AppStatus }
export interface DetailsInput {
  version: number;
  title: string;
  url: string;
  location: string;
  foundVia: string;
  workModel: WorkModel;
  officeDays: string;
  teamSize: string;
  workingHours: string;
  salaryPosted: string;
  salaryAsked: string;
  salaryOffered: string;
  notes: string;
}
export interface CompanyInput {
  version: number;
  name: string;
  website: string;
  officeLocation: string;
  companySize: string;
  annualLeave: string;
  benefits: string;
  notes: string;
}
export interface StatusInput { status: AppStatus; on: string | null; note: string }
export interface FollowUpInput { action: string; dueOn: string; doneOn: string | null }
export interface ContactInput { name: string; role: string; email: string; phone: string; linkedIn: string; notes: string }
export interface ListParams { status?: string; q?: string; sort?: TrackerSort }

// Marks a write as coming from the dashboard. The API refuses tracker writes without it, and another website can't add
// it to a request from your browser.
export const CLIENT_HEADER = { 'X-Tracker-Client': 'dashboard' };

function send<T>(method: string, path: string, body?: unknown): Promise<T> {
  if (body instanceof FormData) return request<T>(path, { method, headers: CLIENT_HEADER, body });
  return request<T>(path, {
    method,
    headers: body === undefined ? CLIENT_HEADER : { ...CLIENT_HEADER, 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
}

function query(params: ListParams): string {
  const search = new URLSearchParams();
  Object.entries(params).forEach(([key, value]) => {
    if (value) search.set(key, String(value));
  });
  const text = search.toString();
  return text ? `?${text}` : '';
}

const BASE = '/api/tracker';
const entry = (id: string) => `${BASE}/applications/${encodeURIComponent(id)}`;

export const tracker = {
  status: () => request<TrackerStatus>(`${BASE}/status`),
  list: (params: ListParams) => request<ApplicationRow[]>(`${BASE}/applications${query(params)}`),
  upNext: () => request<UpNextItem[]>(`${BASE}/up-next`),
  index: () => request<TrackerIndex>(`${BASE}/index`),
  companies: () => request<CompanyOption[]>(`${BASE}/companies`),
  detail: (id: string) => request<ApplicationDetail>(entry(id)),
  shortlist: (postingId: string) => send<NewEntry>('POST', `${BASE}/applications`, { postingId }),
  paste: (input: PasteInput) => send<NewEntry>('POST', `${BASE}/applications/paste`, input),
  setStatus: (id: string, input: StatusInput) => send<void>('POST', `${entry(id)}/status`, input),
  editDetails: (id: string, input: DetailsInput) => send<void>('PATCH', entry(id), input),
  editCompany: (companyId: string, input: CompanyInput) => send<void>('PATCH', `${BASE}/companies/${encodeURIComponent(companyId)}`, input),
  addFollowUp: (id: string, input: FollowUpInput) => send<NewEntry>('POST', `${entry(id)}/follow-ups`, input),
  editFollowUp: (id: string, followUpId: string, input: FollowUpInput) => send<void>('PATCH', `${entry(id)}/follow-ups/${followUpId}`, input),
  deleteFollowUp: (id: string, followUpId: string) => send<void>('DELETE', `${entry(id)}/follow-ups/${followUpId}`),
  addContact: (id: string, input: ContactInput) => send<NewEntry>('POST', `${entry(id)}/contacts`, input),
  deleteContact: (id: string, contactId: string) => send<void>('DELETE', `${entry(id)}/contacts/${contactId}`),
  upload: (id: string, form: FormData) => send<NewEntry>('POST', `${entry(id)}/documents`, form),
  documentUrl: (documentId: string) => `${BASE}/documents/${documentId}`,
  deleteDocument: (documentId: string) => send<void>('DELETE', `${BASE}/documents/${documentId}`),
  remove: (id: string) => send<void>('DELETE', entry(id)),
  cvStatus: () => request<CvStatus>(`${BASE}/cv`),
  fullCvUrl: `${BASE}/cv/full`,
  proposal: (id: string) => request<CvProposal>(`${entry(id)}/cv`),
  savePostingText: (id: string, input: { postingText: string; version: number }) => send<void>('PUT', `${entry(id)}/posting-text`, input),
  generateCv: (id: string, choices: CvChoices) => send<GeneratedCv>('POST', `${entry(id)}/cv`, choices),
};
