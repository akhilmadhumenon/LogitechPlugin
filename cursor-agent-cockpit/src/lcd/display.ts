import type { CommandAction } from '@logitech/plugin-sdk';

type SendCapable = {
  _sendMessage?: (message: unknown) => void;
};

/**
 * Node SDK beta: LPS requests label text via GetActionText → action.displayName.
 * ActionTextChanged exists in the wire protocol but is not publicly exported.
 * We mutate displayName and best-effort emit ActionTextChanged through the SDK
 * private send path so LCD buttons refresh when the host supports it.
 */
export function setActionLabel(action: CommandAction, label: string): void {
  action.displayName = label;
  notifyActionTextChanged(action);
}

function notifyActionTextChanged(action: CommandAction): void {
  const manage = action as CommandAction & { _plugin?: unknown };
  const plugin = manage._plugin as SendCapable | null | undefined;
  if (!plugin?._sendMessage) return;

  try {
    plugin._sendMessage({
      name: 'ActionTextChanged',
      messageType: 'Event',
      parameters: {
        actionName: action.name,
        text: action.displayName,
      },
    });
  } catch {
    // Host may ignore unknown events; displayName still updates on next poll.
  }
}
