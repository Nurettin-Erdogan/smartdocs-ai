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
  return (
    <div
      className="conversation-thread"
      role="log"
      aria-live="polite"
      aria-busy={isLoading}
      aria-label="Sohbet mesajları"
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
