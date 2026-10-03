import { mkdir, readFile, writeFile } from 'fs/promises';
import { dirname } from 'path';
import { METRICS_PATH } from '../config.js';
import type { DailyUsage, UsageMetricView } from './types.js';

function todayKey(): string {
  return new Date().toISOString().slice(0, 10);
}

function emptyDay(date = todayKey()): DailyUsage {
  return {
    date,
    prompts: 0,
    toolCalls: 0,
    sessions: 0,
    errors: 0,
    completedRuns: 0,
  };
}

export class UsageMetrics {
  private day: DailyUsage = emptyDay();
  private view: UsageMetricView = 'prompts';
  private recentModels: string[] = [];
  private loaded = false;

  async load(): Promise<void> {
    if (this.loaded) return;
    try {
      const raw = JSON.parse(await readFile(METRICS_PATH, 'utf8')) as {
        day?: DailyUsage;
        recentModels?: string[];
        view?: UsageMetricView;
      };
      if (raw.day?.date === todayKey()) {
        this.day = raw.day;
      } else {
        this.day = emptyDay();
      }
      this.recentModels = raw.recentModels ?? [];
      this.view = raw.view ?? 'prompts';
    } catch {
      this.day = emptyDay();
    }
    this.loaded = true;
  }

  private async persist(): Promise<void> {
    await mkdir(dirname(METRICS_PATH), { recursive: true });
    await writeFile(
      METRICS_PATH,
      JSON.stringify(
        {
          day: this.day,
          recentModels: this.recentModels,
          view: this.view,
        },
        null,
        2,
      ),
      'utf8',
    );
  }

  private rollDay(): void {
    const key = todayKey();
    if (this.day.date !== key) {
      this.day = emptyDay(key);
    }
  }

  async recordPrompt(model?: string): Promise<void> {
    await this.load();
    this.rollDay();
    this.day.prompts += 1;
    if (model) this.trackModel(model);
    await this.persist();
  }

  async recordToolCall(): Promise<void> {
    await this.load();
    this.rollDay();
    this.day.toolCalls += 1;
    await this.persist();
  }

  async recordSessionStart(model?: string): Promise<void> {
    await this.load();
    this.rollDay();
    this.day.sessions += 1;
    if (model) this.trackModel(model);
    await this.persist();
  }

  async recordError(): Promise<void> {
    await this.load();
    this.rollDay();
    this.day.errors += 1;
    await this.persist();
  }

  async recordCompletion(status?: string, model?: string): Promise<void> {
    await this.load();
    this.rollDay();
    this.day.completedRuns += 1;
    if (status === 'error') this.day.errors += 1;
    if (model) this.trackModel(model);
    await this.persist();
  }

  private trackModel(model: string): void {
    this.recentModels = [model, ...this.recentModels.filter((m) => m !== model)].slice(0, 8);
  }

  cycleView(): UsageMetricView {
    const order: UsageMetricView[] = ['prompts', 'tools', 'model', 'errors', 'sessions'];
    const idx = order.indexOf(this.view);
    this.view = order[(idx + 1) % order.length]!;
    void this.persist();
    return this.view;
  }

  getView(): UsageMetricView {
    return this.view;
  }

  getDay(): DailyUsage {
    this.rollDay();
    return this.day;
  }

  getLastModel(): string | null {
    return this.recentModels[0] ?? null;
  }

  formatLcd(currentModel?: string | null): string {
    const day = this.getDay();
    switch (this.view) {
      case 'prompts':
        return `Prompts\n${day.prompts}`;
      case 'tools':
        return `Tools\n${day.toolCalls}`;
      case 'model':
        return `Model\n${shortModel(currentModel ?? this.getLastModel())}`;
      case 'errors':
        return `Errors\n${day.errors}`;
      case 'sessions':
        return `Sessions\n${day.sessions}`;
    }
  }
}

function shortModel(model: string | null | undefined): string {
  if (!model) return '—';
  const cleaned = model
    .replace(/^claude-/, '')
    .replace(/^gpt-/, '')
    .replace(/-thinking.*$/, '')
    .replace(/-\d{8}.*$/, '');
  return cleaned.length > 12 ? `${cleaned.slice(0, 11)}…` : cleaned;
}

export const usageMetrics = new UsageMetrics();
