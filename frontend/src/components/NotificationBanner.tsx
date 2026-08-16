export type Notification = {
  kind: 'success' | 'error' | 'info';
  message: string;
};

type NotificationBannerProps = {
  notification: Notification | null;
  onDismiss: () => void;
};

export function NotificationBanner({ notification, onDismiss }: NotificationBannerProps) {
  if (!notification) return null;

  return (
    <div
      className={`toast ${notification.kind}`}
      role={notification.kind === 'error' ? 'alert' : 'status'}
      aria-live={notification.kind === 'error' ? 'assertive' : 'polite'}
    >
      <span>{notification.message}</span>
      <button type="button" onClick={onDismiss} aria-label="Bildirimi kapat">
        ×
      </button>
    </div>
  );
}
