import type { CvProposal } from '../api';

// A small proposal for tests: two roles, one skill group, one certification.
// Suggested: a1, a2 (Acme, max 2) and g1. Posting skills: .NET and Docker in a ticked bullet, Azure only on the
// certification, Kubernetes only in the unticked a3, SQL Server nowhere (profile says Working).
export const PROPOSAL: CvProposal = {
  entryId: 'a1', title: 'Senior .NET Developer', company: 'RBC', textSource: 'entry', thin: false, version: 42,
  postingSkills: [
    { skill: '.NET', weight: 2, level: 'Strong' },
    { skill: 'Azure', weight: 1, level: 'Working' },
    { skill: 'Docker', weight: 1, level: 'None' },
    { skill: 'Kubernetes', weight: 1, level: 'Learning' },
    { skill: 'SQL Server', weight: 1, level: 'Working' },
  ],
  headline: 'Senior .NET Developer',
  summaries: [
    { id: 'dotnet', families: ['dotnet', 'fullstack'], text: 'Builds .NET systems.' },
    { id: 'lead', families: ['lead_manager'], text: 'Leads small teams.' },
  ],
  suggestedSummaryId: 'dotnet',
  skillGroups: [{ name: 'Backend', items: [{ text: 'C# / .NET', skills: ['.NET', 'C#'], matches: true }, { text: 'SQL', skills: ['SQL'], matches: false }] }],
  sections: [
    {
      id: 'acme', kind: 'Role', title: 'Senior Developer', company: 'Acme', location: 'Toronto, ON', dates: 'Oct 2023 – Present', maxBullets: 2,
      bullets: [
        { id: 'a1', text: 'Led the team on .NET.', skills: ['.NET'], score: 2, always: true },
        { id: 'a2', text: 'Ran Docker in production.', skills: ['Docker'], score: 1, always: false },
        { id: 'a3', text: 'Moved services to Kubernetes.', skills: ['Kubernetes'], score: 1, always: false },
      ],
    },
    {
      id: 'globex', kind: 'Role', title: 'Developer', company: 'Globex', location: '', dates: 'Jun 2016 – Sep 2023', maxBullets: 3,
      bullets: [{ id: 'g1', text: 'Wrote APIs.', skills: ['REST APIs'], score: 0, always: false }],
    },
  ],
  certifications: [{ name: 'AZ-204', year: '2026', skills: ['Azure'] }],
  suggestion: { headline: 'Senior .NET Developer', summary: 'Builds .NET systems.', bulletIds: ['a1', 'a2', 'g1'] },
  saved: null,
  fixedWords: 100,
};
