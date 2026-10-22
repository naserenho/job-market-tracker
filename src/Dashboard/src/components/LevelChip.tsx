import type { SkillLevel } from '../api';

export function LevelChip({ level }: { level: SkillLevel }) {
  return <span className={`chip chip-${level}`}>{level === 'None' ? 'Gap' : level}</span>;
}
