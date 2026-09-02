import { useEffect, useRef } from 'react';
import type { ChatConversation } from '../api';

type ConversationThreadProps = {
  conversation: ChatConversation | null;
  isLoading: boolean;
  isNewConversation: boolean;
};

const formatMessageDate = (value: string) =>
  new Date(value).toLocaleString('tr-TR', {
    dateStyle: 'short',
    timeStyle: 'short'
  });

export function ConversationThread({
  conversation,
  isLoading,
  isNewConversation
}: ConversationThreadProps) {
  const threadRef = useRef<HTMLDivElement>(null);
  const shouldFollowLatestRef = useRef(true);
  const previousConversationIdRef = useRef<number | null>(null);
  const previousMessageCountRef = useRef(0);
  const messages = conversation?.messages ?? [];
  const latestMessage = messages[messages.length - 1];

  useEffect(() => {
    const conversationChanged = previousConversationIdRef.current !== (conversation?.conversationId ?? null);
    const newMessageAdded = messages.length > previousMessageCountRef.current;

    if (conversationChanged || newMessageAdded) {
      shouldFollowLatestRef.current = true;
    }

    previousConversationIdRef.current = conversation?.conversationId ?? null;
    previousMessageCountRef.current = messages.length;

    if (!shouldFollowLatestRef.current) return;

    const frame = window.requestAnimationFrame(() => {
      const thread = threadRef.current;
      if (thread) thread.scrollTop = thread.scrollHeight;
    });

    return () => window.cancelAnimationFrame(frame);
  }, [conversation?.conversationId, isLoading, latestMessage?.answer, latestMessage?.id, messages.length]);

  return (
    <div
      ref={threadRef}
      className="conversation-thread"
      role="log"
      aria-live="polite"
      aria-busy={isLoading}
      aria-label="Sohbet mesajları"
      onScroll={(event) => {
        const thread = event.currentTarget;
        const distanceFromBottom = thread.scrollHeight - thread.scrollTop - thread.clientHeight;
        shouldFollowLatestRef.current = distanceFromBottom < 72;
      }}
    >
      {isLoading && <p className="muted conversation-empty">Sohbet yükleniyor...</p>}

      {!isLoading && isNewConversation && (
        <div className="conversation-empty">
          <strong>Yeni bir sohbet başlat.</strong>
          <p className="muted">Sorun, hazır durumdaki PDF belgelerinde aranacak.</p>
        </div>
      )}

      {!isLoading && !isNewConversation && !conversation && (
        <p className="muted conversation-empty">Görüntülemek için bir sohbet seç.</p>
      )}

      {!isLoading && conversation?.messages.length === 0 && (
        <p className="muted conversation-empty">Bu sohbette henüz mesaj yok.</p>
      )}

      {!isLoading && conversation?.messages.map((message) => (
        <article className="message-pair" key={message.id}>
          <div className="chat-message user-message">
            <div className="message-head">
              <strong>Sen</strong>
              <time dateTime={message.createdAt}>{formatMessageDate(message.createdAt)}</time>
            </div>
            <p>{message.question}</p>
          </div>

          <div className="chat-message assistant-message">
            <div className="message-head">
              <strong>SmartDocs AI</strong>
            </div>
            <p>{message.answer}</p>
          </div>
        </article>
      ))}
    </div>
  );
}
