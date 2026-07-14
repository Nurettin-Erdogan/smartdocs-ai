import {
  FormEvent,
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState
} from 'react';
import {
  api,
  ChatConversation,
  ChatHistorySummary,
  ChatResponse,
  DocumentItem,
  setUnauthorizedHandler
} from './api';
import { ConversationThread } from './components/ConversationThread';
import {
  NotificationBanner,
  type Notification
} from './components/NotificationBanner';
import {
  clearSession,
  loadSession,
  saveSession,
  type AppSession,
  type SessionUser
} from './session';

type AuthMode = 'login' | 'register';
type DocumentAction = { id: number; kind: 'delete' | 'reindex' } | null;

const MAX_PDF_SIZE = 20 * 1024 * 1024;

const formatDate = (value: string) =>
  new Date(value).toLocaleString('tr-TR', {
    dateStyle: 'medium',
    timeStyle: 'short'
  });

const formatSize = (size: number) => {
  if (size < 1024) return `${size} B`;
  if (size < 1024 * 1024) return `${(size / 1024).toFixed(1)} KB`;
  return `${(size / (1024 * 1024)).toFixed(1)} MB`;
};

const formatIndexingStatus = (status: string) => {
  switch (status) {
    case 'Ready': return 'Hazır';
    case 'Failed': return 'İndekslenemedi';
    case 'NoContent': return 'Metin bulunamadı';
    case 'Pending': return 'İşleniyor';
    default: return status || 'Bilinmiyor';
  }
};

const errorMessage = (error: unknown, fallback: string) =>
  error instanceof Error ? error.message : fallback;

const conversationTitle = (conversation: ChatHistorySummary) => {
  const question = conversation.firstQuestion.trim();
  if (!question) return `Sohbet #${conversation.conversationId}`;
  return question.length > 42 ? `${question.slice(0, 42)}…` : question;
};

function App() {
  const [session, setSession] = useState<AppSession | null>(() => loadSession());
  const [authMode, setAuthMode] = useState<AuthMode>('login');
  const [documents, setDocuments] = useState<DocumentItem[]>([]);
  const [history, setHistory] = useState<ChatHistorySummary[]>([]);
  const [selectedConversationId, setSelectedConversationId] = useState<number | null>(null);
  const [selectedConversation, setSelectedConversation] = useState<ChatConversation | null>(null);
  const [question, setQuestion] = useState('');
  const [sources, setSources] = useState<ChatResponse['sources']>([]);
  const [authForm, setAuthForm] = useState({ fullName: '', email: '', password: '' });
  const [loginForm, setLoginForm] = useState({ email: '', password: '' });
  const [uploadFile, setUploadFile] = useState<File | null>(null);
  const [notification, setNotification] = useState<Notification | null>(null);
  const [authBusy, setAuthBusy] = useState(false);
  const [refreshing, setRefreshing] = useState(false);
  const [uploading, setUploading] = useState(false);
  const [asking, setAsking] = useState(false);
  const [conversationLoading, setConversationLoading] = useState(false);
  const [documentAction, setDocumentAction] = useState<DocumentAction>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);

  const resetWorkspace = useCallback(() => {
    setDocuments([]);
    setHistory([]);
    setSelectedConversationId(null);
    setSelectedConversation(null);
    setQuestion('');
    setSources([]);
    setUploadFile(null);
    if (fileInputRef.current) fileInputRef.current.value = '';
  }, []);

  const expireSession = useCallback((message: string) => {
    clearSession();
    setSession(null);
    resetWorkspace();
    setNotification({ kind: 'error', message });
  }, [resetWorkspace]);

  useEffect(() => setUnauthorizedHandler(expireSession), [expireSession]);

  useEffect(() => {
    if (!notification) return;
    const timer = window.setTimeout(() => setNotification(null), 5_000);
    return () => window.clearTimeout(timer);
  }, [notification]);

  const refreshData = useCallback(async (selectLatestWhenEmpty = false) => {
    if (!session) return;

    setRefreshing(true);
    try {
      const [nextDocuments, nextHistory] = await Promise.all([
        api.listDocuments(),
        api.chatHistory()
      ]);
      setDocuments(nextDocuments);
      setHistory(nextHistory);
      setSelectedConversationId((currentId) => {
        if (currentId !== null && nextHistory.some((item) => item.conversationId === currentId)) {
          return currentId;
        }
        return selectLatestWhenEmpty ? nextHistory[0]?.conversationId ?? null : null;
      });
    } catch (error) {
      setNotification({ kind: 'error', message: errorMessage(error, 'Veriler yüklenemedi.') });
    } finally {
      setRefreshing(false);
    }
  }, [session]);

  useEffect(() => {
    if (session) void refreshData(true);
  }, [session, refreshData]);

  useEffect(() => {
    if (!session || selectedConversationId === null) {
      setSelectedConversation(null);
      setConversationLoading(false);
      return;
    }

    let cancelled = false;
    setConversationLoading(true);

    void api.getConversation(selectedConversationId)
      .then((conversation) => {
        if (!cancelled) setSelectedConversation(conversation);
      })
      .catch((error) => {
        if (!cancelled) {
          setNotification({
            kind: 'error',
            message: errorMessage(error, 'Sohbet yüklenemedi.')
          });
        }
      })
      .finally(() => {
        if (!cancelled) setConversationLoading(false);
      });

    return () => {
      cancelled = true;
    };
  }, [session, selectedConversationId]);

  const dashboardStats = useMemo(() => {
    const totalMessages = history.reduce((count, item) => count + item.messageCount, 0);
    return [
      { label: 'Toplam doküman', value: String(documents.length) },
      { label: 'Son 50 sohbet', value: String(history.length) },
      { label: 'Mesaj', value: String(totalMessages) },
      { label: 'Son yükleme', value: documents[0]?.title ?? 'Yok' }
    ];
  }, [documents, history]);

  const persistAuth = (token: string, user: SessionUser) => {
    const nextSession = { token, user };
    saveSession(nextSession);
    setSession(nextSession);
  };

  const handleLogin = async (event: FormEvent) => {
    event.preventDefault();
    setAuthBusy(true);
    setNotification(null);
    try {
      const result = await api.login(loginForm);
      persistAuth(result.token, {
        fullName: result.fullName,
        email: result.email,
        role: result.role
      });
      setNotification({ kind: 'success', message: 'Giriş başarılı.' });
    } catch (error) {
      setNotification({ kind: 'error', message: errorMessage(error, 'Giriş başarısız oldu.') });
    } finally {
      setAuthBusy(false);
    }
  };

  const handleRegister = async (event: FormEvent) => {
    event.preventDefault();
    setAuthBusy(true);
    setNotification(null);
    try {
      const result = await api.register(authForm);
      persistAuth(result.token, {
        fullName: result.fullName,
        email: result.email,
        role: result.role
      });
      setNotification({ kind: 'success', message: 'Hesabın oluşturuldu.' });
    } catch (error) {
      setNotification({ kind: 'error', message: errorMessage(error, 'Kayıt başarısız oldu.') });
    } finally {
      setAuthBusy(false);
    }
  };

  const handleFileChange = (file: File | null) => {
    setNotification(null);
    if (!file) {
      setUploadFile(null);
      return;
    }

    if (!file.name.toLowerCase().endsWith('.pdf')) {
      setUploadFile(null);
      if (fileInputRef.current) fileInputRef.current.value = '';
      setNotification({ kind: 'error', message: 'Yalnızca PDF dosyası seçebilirsin.' });
      return;
    }

    if (file.size > MAX_PDF_SIZE) {
      setUploadFile(null);
      if (fileInputRef.current) fileInputRef.current.value = '';
      setNotification({ kind: 'error', message: 'PDF dosyası en fazla 20 MB olabilir.' });
      return;
    }

    setUploadFile(file);
  };

  const handleUpload = async () => {
    if (!uploadFile) {
      setNotification({ kind: 'error', message: 'Önce bir PDF seçmelisin.' });
      return;
    }

    setUploading(true);
    setNotification(null);
    try {
      const result = await api.uploadDocument(uploadFile);
      setNotification({
        kind: result.indexingStatus === 'Ready' ? 'success' : 'info',
        message: result.indexingStatus === 'Ready'
          ? 'PDF yüklendi ve sohbete hazır.'
          : `PDF yüklendi: ${formatIndexingStatus(result.indexingStatus ?? 'Pending')}.`
      });
      setUploadFile(null);
      if (fileInputRef.current) fileInputRef.current.value = '';
      await refreshData(false);
    } catch (error) {
      setNotification({ kind: 'error', message: errorMessage(error, 'Yükleme başarısız oldu.') });
    } finally {
      setUploading(false);
    }
  };

  const handleDelete = async (document: DocumentItem) => {
    const confirmed = window.confirm(
      `“${document.title}” belgesi, dosyası ve arama indeksi kalıcı olarak silinsin mi?`
    );
    if (!confirmed) return;

    setDocumentAction({ id: document.id, kind: 'delete' });
    setNotification(null);
    try {
      await api.deleteDocument(document.id);
      setNotification({ kind: 'success', message: 'Belge silindi.' });
      await refreshData(false);
    } catch (error) {
      setNotification({ kind: 'error', message: errorMessage(error, 'Belge silinemedi.') });
    } finally {
      setDocumentAction(null);
    }
  };

  const handleReindex = async (id: number) => {
    setDocumentAction({ id, kind: 'reindex' });
    setNotification(null);
    try {
      await api.reindexDocument(id);
      setNotification({ kind: 'success', message: 'Belge yeniden indekslendi.' });
      await refreshData(false);
    } catch (error) {
      setNotification({ kind: 'error', message: errorMessage(error, 'Belge yeniden indekslenemedi.') });
      await refreshData(false);
    } finally {
      setDocumentAction(null);
    }
  };

  const handleAsk = async (event: FormEvent) => {
    event.preventDefault();
    const trimmedQuestion = question.trim();
    if (!trimmedQuestion) {
      setNotification({ kind: 'error', message: 'Soru yazmalısın.' });
      return;
    }

    if (!documents.some((document) => document.indexingStatus === 'Ready')) {
      setNotification({ kind: 'error', message: 'Önce hazır durumunda bir PDF yüklemelisin.' });
      return;
    }

    setAsking(true);
    setNotification(null);
    try {
      const result = await api.askChat({
        question: trimmedQuestion,
        conversationId: selectedConversationId
      });
      const createdAt = new Date().toISOString();
      setSelectedConversation((current) => ({
        conversationId: result.conversationId,
        createdAt: current?.conversationId === result.conversationId
          ? current.createdAt
          : createdAt,
        messages: [
          ...(current?.conversationId === result.conversationId ? current.messages : []),
          {
            id: -Date.now(),
            question: trimmedQuestion,
            answer: result.answer,
            createdAt
          }
        ]
      }));
      setSelectedConversationId(result.conversationId);
      setSources(result.sources);
      setQuestion('');
      setNotification({ kind: 'success', message: 'Cevap hazır.' });

      await refreshData(false);
      try {
        setSelectedConversation(await api.getConversation(result.conversationId));
      } catch {
        // The optimistic message remains visible if refreshing the saved conversation fails.
      }
    } catch (error) {
      setNotification({ kind: 'error', message: errorMessage(error, 'Soru gönderilemedi.') });
    } finally {
      setAsking(false);
    }
  };

  const handleLogout = () => {
    clearSession();
    setSession(null);
    resetWorkspace();
    setNotification({ kind: 'info', message: 'Çıkış yapıldı.' });
  };

  const handleNewConversation = () => {
    setSelectedConversationId(null);
    setSelectedConversation(null);
    setSources([]);
    setQuestion('');
    setNotification({ kind: 'info', message: 'Yeni sohbet hazır.' });
  };

  if (!session) {
    return (
      <div className="auth-shell">
        <div className="auth-backdrop" />
        <main className="auth-card">
          <section className="hero-block">
            <div className="brand-pill">SmartDocs AI</div>
            <h1>PDF belgelerini yükle, sor, kaynaklı cevap al.</h1>
            <p>
              Belgelerin kendi altyapında kalır; SmartDocs AI yalnızca yüklediğin içeriklerden
              anlamlı kaynaklar bulup Türkçe cevap üretir.
            </p>
            <div className="feature-row">
              <span>Kullanıcıya özel</span>
              <span>Kaynak gösterimi</span>
              <span>Yerel yapay zekâ</span>
            </div>
          </section>

          <section className="panel auth-panel" aria-labelledby="auth-title">
            <h2 id="auth-title" className="sr-only">
              {authMode === 'login' ? 'Giriş yap' : 'Hesap oluştur'}
            </h2>
            <div className="mode-switch">
              <button
                className={authMode === 'login' ? 'active' : ''}
                onClick={() => setAuthMode('login')}
                type="button"
              >
                Giriş Yap
              </button>
              <button
                className={authMode === 'register' ? 'active' : ''}
                onClick={() => setAuthMode('register')}
                type="button"
              >
                Kayıt Ol
              </button>
            </div>

            <form onSubmit={authMode === 'login' ? handleLogin : handleRegister} className="stack">
              {authMode === 'register' && (
                <label>
                  Ad Soyad
                  <input
                    value={authForm.fullName}
                    onChange={(event) => setAuthForm({ ...authForm, fullName: event.target.value })}
                    autoComplete="name"
                    maxLength={100}
                    required
                  />
                </label>
              )}
              <label>
                E-posta
                <input
                  value={authMode === 'login' ? loginForm.email : authForm.email}
                  onChange={(event) => authMode === 'login'
                    ? setLoginForm({ ...loginForm, email: event.target.value })
                    : setAuthForm({ ...authForm, email: event.target.value })}
                  type="email"
                  autoComplete="email"
                  placeholder="ornek@firma.com"
                  required
                />
              </label>
              <label>
                Şifre
                <input
                  value={authMode === 'login' ? loginForm.password : authForm.password}
                  onChange={(event) => authMode === 'login'
                    ? setLoginForm({ ...loginForm, password: event.target.value })
                    : setAuthForm({ ...authForm, password: event.target.value })}
                  type="password"
                  autoComplete={authMode === 'login' ? 'current-password' : 'new-password'}
                  minLength={authMode === 'register' ? 8 : undefined}
                  maxLength={128}
                  required
                />
              </label>
              <button disabled={authBusy} className="primary-btn" type="submit">
                {authBusy ? 'İşleniyor…' : authMode === 'login' ? 'Giriş Yap' : 'Hesap Oluştur'}
              </button>
            </form>
          </section>
        </main>

        <NotificationBanner
          notification={notification}
          onDismiss={() => setNotification(null)}
        />
      </div>
    );
  }

  const user = session.user;
  const anyDocumentAction = documentAction !== null;

  return (
    <div className="app-shell">
      <aside className="sidebar panel">
        <div>
          <div className="brand-pill">SmartDocs AI</div>
          <h2>{user.fullName}</h2>
          <p>{user.email}</p>
          <div className="role-tag">{user.role ?? 'Kullanıcı'}</div>
        </div>

        <div className="stat-grid">
          {dashboardStats.map((item) => (
            <article key={item.label} className="stat-card">
              <span>{item.label}</span>
              <strong title={item.value}>{item.value}</strong>
            </article>
          ))}
        </div>

        <div className="panel-subsection">
          <div className="section-head">
            <h3>Belge yükle</h3>
            <button
              type="button"
              className="ghost-btn"
              onClick={() => void refreshData(false)}
              disabled={refreshing}
            >
              {refreshing ? 'Yenileniyor…' : 'Yenile'}
            </button>
          </div>
          <label className="file-label">
            PDF seç
            <input
              ref={fileInputRef}
              type="file"
              accept="application/pdf,.pdf"
              aria-describedby="upload-help"
              onChange={(event) => handleFileChange(event.target.files?.[0] ?? null)}
            />
          </label>
          <small id="upload-help" className="muted">En fazla 20 MB · yalnızca PDF</small>
          <button
            type="button"
            className="primary-btn"
            onClick={() => void handleUpload()}
            disabled={uploading || !uploadFile}
          >
            {uploading ? 'İşleniyor…' : 'PDF Yükle'}
          </button>
        </div>

        <div className="panel-subsection history-list">
          <div className="section-head">
            <h3>Sohbet geçmişi</h3>
            <button type="button" className="ghost-btn" onClick={handleNewConversation}>
              Yeni
            </button>
          </div>
          <div className="scroll-list">
            {history.length === 0 && <p className="muted">Henüz sohbet yok.</p>}
            {history.map((conversation) => (
              <button
                key={conversation.conversationId}
                type="button"
                className={`history-item ${selectedConversationId === conversation.conversationId ? 'active' : ''}`}
                onClick={() => {
                  setSources([]);
                  setSelectedConversationId(conversation.conversationId);
                }}
                aria-current={selectedConversationId === conversation.conversationId ? 'true' : undefined}
              >
                <strong>{conversationTitle(conversation)}</strong>
                <span>{formatDate(conversation.createdAt)}</span>
                <small>{conversation.messageCount} mesaj</small>
              </button>
            ))}
          </div>
        </div>

        <button type="button" className="ghost-btn danger" onClick={handleLogout}>
          Çıkış Yap
        </button>
      </aside>

      <main className="main-grid">
        <section className="panel documents-panel">
          <div className="section-head">
            <div>
              <h3>Dokümanlar</h3>
              <p className="muted">Yüklenen PDF’ler ve indeksleme durumu.</p>
            </div>
            <span className="count-badge">{documents.length}</span>
          </div>

          <div className="table-list">
            {documents.length === 0 && <p className="muted">Henüz belge yok.</p>}
            {documents.map((document) => (
              <article key={document.id} className="doc-row">
                <div>
                  <strong>{document.title}</strong>
                  <p>{document.fileName}</p>
                </div>
                <div className="doc-meta">
                  <span>{formatSize(document.fileSize)}</span>
                  <span className={`status-pill status-${document.indexingStatus.toLowerCase()}`}>
                    {formatIndexingStatus(document.indexingStatus)}
                  </span>
                  <span>{formatDate(document.uploadDate)}</span>
                </div>
                <div className="doc-actions">
                  {document.indexingStatus === 'Failed' && (
                    <button
                      type="button"
                      className="ghost-btn retry"
                      onClick={() => void handleReindex(document.id)}
                      disabled={anyDocumentAction}
                    >
                      {documentAction?.id === document.id && documentAction.kind === 'reindex'
                        ? 'İndeksleniyor…'
                        : 'Tekrar indeksle'}
                    </button>
                  )}
                  <button
                    type="button"
                    className="ghost-btn danger"
                    onClick={() => void handleDelete(document)}
                    disabled={anyDocumentAction}
                  >
                    {documentAction?.id === document.id && documentAction.kind === 'delete'
                      ? 'Siliniyor…'
                      : 'Sil'}
                  </button>
                </div>
              </article>
            ))}
          </div>
        </section>

        <section className="panel chat-panel">
          <div className="section-head">
            <div>
              <h3>Belge Asistanı</h3>
              <p className="muted">Soru sor; yanıtı ve kullanılan kaynakları birlikte gör.</p>
            </div>
            <span className="count-badge">{selectedConversationId ?? 'Yeni'}</span>
          </div>

          <ConversationThread
            conversation={selectedConversation}
            isLoading={conversationLoading}
            isNewConversation={selectedConversationId === null}
          />

          <form onSubmit={handleAsk} className="chat-form">
            <label htmlFor="chat-question" className="sr-only">Sorun</label>
            <textarea
              id="chat-question"
              value={question}
              onChange={(event) => setQuestion(event.target.value)}
              onKeyDown={(event) => {
                if (event.key === 'Enter' && !event.shiftKey) {
                  event.preventDefault();
                  event.currentTarget.form?.requestSubmit();
                }
              }}
              placeholder="Sorunu yaz… Enter ile gönder, Shift+Enter ile satır ekle"
              rows={4}
              maxLength={2_000}
              disabled={asking}
            />
            <div className="composer-footer">
              <small className="muted">{question.length}/2000</small>
              <button className="primary-btn" type="submit" disabled={asking || !question.trim()}>
                {asking ? 'Cevap hazırlanıyor…' : 'Soruyu Gönder'}
              </button>
            </div>
          </form>

          <div className="sources-box">
            <div className="section-head compact">
              <h4>Son cevabın kaynakları</h4>
              <span>{sources.length}</span>
            </div>
            {sources.length === 0 && (
              <p className="muted">
                Yeni bir cevap üretildiğinde kullanılan belge parçaları burada görünür.
              </p>
            )}
            <div className="source-list">
              {sources.map((source) => (
                <article
                  key={`${source.documentId}-${source.chunkIndex}-${source.pageNumber}`}
                  className="source-card"
                >
                  <strong>{source.title} · Sayfa {source.pageNumber}</strong>
                  <p>{source.content}</p>
                  <small>Parça {source.chunkIndex} · Skor {source.score.toFixed(3)}</small>
                </article>
              ))}
            </div>
          </div>
        </section>
      </main>

      <NotificationBanner
        notification={notification}
        onDismiss={() => setNotification(null)}
      />
    </div>
  );
}

export default App;
