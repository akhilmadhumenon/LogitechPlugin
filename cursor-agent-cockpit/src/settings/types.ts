export type KeyModifier = 'command' | 'control' | 'option' | 'shift';

export type Keystroke = {
  key: string;
  modifiers?: KeyModifier[];
};

export type KeyboardShortcut = {
  id: string;
  label: string;
  description?: string;
  sequence: Keystroke[];
};

export type PromptShortcut = {
  id: string;
  label: string;
  prompt: string;
  /** Where to send the prompt. */
  target: 'agent' | 'chat' | 'inline';
  /** Press Enter after typing. */
  submit?: boolean;
};

export type ModelOption = {
  id: string;
  label: string;
  /** Keystroke sequence that selects this model in Cursor. */
  selectSequence: Keystroke[];
};

export type ModeOption = {
  id: 'agent' | 'ask' | 'edit';
  label: string;
  selectSequence: Keystroke[];
};

export type PluginSettings = {
  version: 1;
  keyboardShortcuts: KeyboardShortcut[];
  promptShortcuts: PromptShortcut[];
  models: ModelOption[];
  modes: ModeOption[];
  /** Chord used to open the model picker before cycling. */
  openModelPicker?: Keystroke[];
  /** Chord used to open the mode picker before cycling. */
  openModePicker?: Keystroke[];
};

export const DEFAULT_SETTINGS: PluginSettings = {
  version: 1,
  keyboardShortcuts: [
    {
      id: 'command-palette',
      label: 'Cmd Palette',
      description: 'Open Cursor command palette',
      sequence: [{ key: 'p', modifiers: ['command', 'shift'] }],
    },
    {
      id: 'quick-open',
      label: 'Quick Open',
      sequence: [{ key: 'p', modifiers: ['command'] }],
    },
    {
      id: 'toggle-sidebar',
      label: 'Sidebar',
      sequence: [{ key: 'b', modifiers: ['command'] }],
    },
    {
      id: 'toggle-terminal',
      label: 'Terminal',
      sequence: [{ key: '`', modifiers: ['control'] }],
    },
  ],
  promptShortcuts: [
    {
      id: 'write-tests',
      label: 'Tests',
      prompt: 'Write thorough unit tests for the current file. Cover edge cases.',
      target: 'agent',
      submit: true,
    },
    {
      id: 'explain',
      label: 'Explain',
      prompt: 'Explain the selected code and its responsibilities.',
      target: 'chat',
      submit: true,
    },
    {
      id: 'add-types',
      label: 'Types',
      prompt: 'Add precise TypeScript types to the current file without changing behavior.',
      target: 'agent',
      submit: true,
    },
    {
      id: 'refactor',
      label: 'Refactor',
      prompt: 'Refactor the selection for clarity and maintainability. Keep behavior identical.',
      target: 'agent',
      submit: true,
    },
  ],
  models: [
    {
      id: 'auto',
      label: 'Auto',
      selectSequence: [],
    },
    {
      id: 'sonnet',
      label: 'Sonnet',
      selectSequence: [],
    },
    {
      id: 'opus',
      label: 'Opus',
      selectSequence: [],
    },
  ],
  modes: [
    {
      id: 'agent',
      label: 'Agent',
      selectSequence: [{ key: 'i', modifiers: ['command'] }],
    },
    {
      id: 'ask',
      label: 'Ask',
      selectSequence: [{ key: 'l', modifiers: ['command'] }],
    },
    {
      id: 'edit',
      label: 'Edit',
      selectSequence: [{ key: 'k', modifiers: ['command'] }],
    },
  ],
};
