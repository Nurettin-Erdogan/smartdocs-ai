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
  isDemoMode,
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

const MAX_PDF_SIZE = 100 * 1024 * 1024;
const ACTIVE_DOCUMENT_STATUSES = ['Pending', 'Extracting', 'Indexing', 'RetryWaiting'];

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
    case 'Pending': return 'Sırada bekliyor';
    case 'Extracting': return 'Metin çıkarılıyor';
    case 'Indexing': return 'Yapay zekâ hazırlanıyor';
    case 'RetryWaiting': return 'Otomatik tekrar denenecek';
    case 'Deleting': return 'Silme bekliyor';
    default: return status || 'Bilinmiyor';
  }
};

const errorMessage = (error: unknown, fallback: string) =>
  error instanceof Error ? error.message : fallback;

const isAbortError = (error: unknown) =>
  error instanceof DOMException && error.name === 'AbortError';

const conversationTitle = (conversation: ChatHistorySummary) => {
  const question = conversation.firstQuestion.trim();
  if (!question) return `Sohbet #${conversation.conversationId}`;
  return question.length > 42 ? `${question.slice(0, 42)}…` : question;
};

function App() {
  const [session, setSession] = useState<AppSession | null>(() => loadSession());
  const [authMode, setAuthMode] = useState<AuthMode>('login');
  const [documents, setDocuments] = useState<DocumentItem[]>([]);
  const [selectedDocumentIds, setSelectedDocumentIds] = useState<number[]>([]);
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
  const [isDraggingFile, setIsDraggingFile] = useState(false);
  const [asking, setAsking] = useState(false);
  const [conversationLoading, setConversationLoading] = useState(false);
  const [documentAction, setDocumentAction] = useState<DocumentAction>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);
  const refreshControllerRef = useRef<AbortController | null>(null);
  const chatControllerRef = useRef<AbortController | null>(null);

  const resetWorkspace = useCallback(() => {
    refreshControllerRef.current?.abort();
    refreshControllerRef.current = null;
    chatControllerRef.current?.abort();
    chatControllerRef.current = null;
    setDocuments([]);
    setSelectedDocumentIds([]);
    setHistory([]);
    setSelectedConversationId(null);
    setSelectedConversation(null);
    setQuestion('');
    setSources([]);
    setUploadFile(null);
    setIsDraggingFile(false);
    setRefreshing(false);
    setConversationLoading(false);
    setDocumentAction(null);
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

    refreshControllerRef.current?.abort();
    const controller = new AbortController();
    refreshControllerRef.current = controller;
    setRefreshing(true);
    try {
      const [nextDocuments, nextHistory] = await Promise.all([
        api.listDocuments(controller.signal),
        api.chatHistory(controller.signal)
      ]);
      if (controller.signal.aborted || refreshControllerRef.current !== controller) return;
      setDocuments(nextDocuments);
      setSelectedDocumentIds((currentIds) => {
        const readyIds = nextDocuments
          .filter((document) => document.indexingStatus === 'Ready')
          .map((document) => document.id);
        const availableSelection = currentIds.filter((id) => readyIds.includes(id));
        return availableSelection.length > 0 ? availableSelection : readyIds;
      });
      setHistory(nextHistory);
      setSelectedConversationId((currentId) => {
        if (currentId !== null && nextHistory.some((item) => item.conversationId === currentId)) {
          return currentId;
        }
        return selectLatestWhenEmpty ? nextHistory[0]?.conversationId ?? null : null;
      });
    } catch (error) {
      if (isAbortError(error)) return;
      setNotification({ kind: 'error', message: errorMessage(error, 'Veriler yüklenemedi.') });
    } finally {
      if (refreshControllerRef.current === controller) {
        refreshControllerRef.current = null;
        setRefreshing(false);
      }
    }
  }, [session]);

  useEffect(() => {
    if (session) void refreshData(true);
  }, [session, refreshData]);

  useEffect(() => {
    if (!session || !documents.some((document) =>
      ACTIVE_DOCUMENT_STATUSES.includes(document.indexingStatus))) {
      return;
    }

    const timer = window.setInterval(() => void refreshData(false), 2_000);
    return () => window.clearInterval(timer);
  }, [documents, refreshData, session]);

  useEffect(() => {
    if (!session || selectedConversationId === null) {
      setSelectedConversation(null);
      setConversationLoading(false);
      return;
    }

    const controller = new AbortController();
    setConversationLoading(true);

    void api.getConversation(selectedConversationId, controller.signal)
      .then((conversation) => {
        if (!controller.signal.aborted) setSelectedConversation(conversation);
      })
      .catch((error) => {
        if (!controller.signal.aborted && !isAbortError(error)) {
          setNotification({
            kind: 'error',
            message: errorMessage(error, 'Sohbet yüklenemedi.')
          });
        }
      })
      .finally(() => {
        if (!controller.signal.aborted) setConversationLoading(false);
      });

    return () => {
      controller.abort();
    };
  }, [session, selectedConversationId]);

  const dashboardStats = useMemo(() => {
    const totalMessages = history.reduce((count, item) => count + item.messageCount, 0);
    const readyDocuments = documents.filter((document) => document.indexingStatus === 'Ready').length;
    return [
      { label: 'Toplam doküman', value: String(documents.length) },
      { label: 'Sohbete hazır', value: String(readyDocuments) },
      { label: 'Sohbet', value: String(history.length) },
      { label: 'Toplam mesaj', value: String(totalMessages) }
    ];
  }, [documents, history]);

  const readyDocuments = useMemo(
    () => documents.filter((document) => document.indexingStatus === 'Ready'),
    [documents]
  );

  const persistAuth = (token: string, user: SessionUser) => {
    const nextSession = { token, user };
    saveSession(nextSession);
    setSession(nextSession);
  };

  const handleOpenDemo = () => {
    persistAuth('smartdocs-demo-session', {
      fullName: 'Nurettin Erdoğan',
      email: 'demo@smartdocs.ai',
      role: 'Vitrin kullanıcısı'
    });
    setNotification({
      kind: 'info',
      message: 'Vitrin demosu açıldı. Örnek belgelerle tüm arayüzü deneyebilirsin.'
    });
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
      setLoginForm((current) => ({ ...current, password: '' }));
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
      setAuthForm({ fullName: '', email: '', password: '' });
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
      if (fileInputRef.current) fileInputRef.current.value = '';
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
      setNotification({ kind: 'error', message: 'PDF dosyası en fazla 100 MB olabilir.' });
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
        kind: 'info',
        message: 'PDF alındı. Arka planda hazırlanıyor; bu sırada çalışmaya devam edebilirsin.'
      });
      setUploadFile(null);
      if (result.indexingStatus === 'Ready') {
        setSelectedDocumentIds((currentIds) =>
          currentIds.includes(result.id) ? currentIds : [...currentIds, result.id]);
      }
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
      await refreshData(false);
    } finally {
      setDocumentAction(null);
    }
  };

  const handleReindex = async (id: number) => {
    setDocumentAction({ id, kind: 'reindex' });
    setNotification(null);
    try {
      await api.reindexDocument(id);
      setNotification({ kind: 'info', message: 'Belge yeniden hazırlama kuyruğuna alındı.' });
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

    if (selectedDocumentIds.length === 0) {
      setNotification({ kind: 'error', message: 'Soru sormak için en az bir hazır belge seçmelisin.' });
      return;
    }

    setAsking(true);
    setNotification(null);
    chatControllerRef.current?.abort();
    const chatController = new AbortController();
    chatControllerRef.current = chatController;
    const temporaryMessageId = -Date.now();
    const createdAt = new Date().toISOString();
    try {
      const result = await api.askChat({
        question: trimmedQuestion,
        conversationId: selectedConversationId,
        documentIds: selectedDocumentIds
      }, {
        signal: chatController.signal,
        onStart: ({ conversationId, sources: nextSources }) => {
          setSelectedConversationId(conversationId);
          setSources(nextSources);
          setSelectedConversation((current) => ({
            conversationId,
            createdAt: current?.conversationId === conversationId
              ? current.createdAt
              : createdAt,
            messages: [
              ...(current?.conversationId === conversationId ? current.messages : []),
              {
                id: temporaryMessageId,
                question: trimmedQuestion,
                answer: '',
                createdAt
              }
            ]
          }));
        },
        onChunk: (content) => {
          setSelectedConversation((current) => current ? {
            ...current,
            messages: current.messages.map((message) =>
              message.id === temporaryMessageId
                ? { ...message, answer: message.answer + content }
                : message)
          } : current);
        }
      });
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
      if (isAbortError(error)) {
        setNotification({ kind: 'info', message: 'Cevap üretimi durduruldu.' });
      } else {
        setNotification({ kind: 'error', message: errorMessage(error, 'Soru gönderilemedi.') });
      }
    } finally {
      if (chatControllerRef.current === chatController) {
        chatControllerRef.current = null;
        setAsking(false);
      }
    }
  };

  const handleStopAnswer = () => {
    chatControllerRef.current?.abort();
  };

  const handleLogout = () => {
    clearSession();
    setSession(null);
    setAuthMode('login');
    setLoginForm({ email: '', password: '' });
    setAuthForm({ fullName: '', email: '', password: '' });
    resetWorkspace();
    setNotification({ kind: 'info', message: 'Çıkış yapıldı.' });
  };

  const handleNewConversation = () => {
    chatControllerRef.current?.abort();
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
            <header className="auth-brand">
              <div className="brand-pill"><span className="brand-mark">S</span> SmartDocs AI</div>
              <span className="local-badge"><span className="pulse-dot" /> Yerel ve güvenli</span>
            </header>

            <div className="hero-copy">
              <p className="eyebrow">BELGELERİNİ ANLAYAN YAPAY ZEKÂ</p>
              <h1>Belgeni yükle.<br /><em>Cevabını kaynağından al.</em></h1>
              <p>
                Sayfalar arasında arama yapmakla uğraşma. Sorunu yaz; SmartDocs AI
                yüklediğin belgeleri incelesin, sana açık ve kaynaklı bir cevap hazırlasın.
              </p>
            </div>

            <div className="workflow-card" aria-label="SmartDocs AI çalışma şekli">
              <div className="workflow-step">
                <span className="step-icon">PDF</span>
                <div><b>Belgeni ekle</b><small>PDF dosyanı güvenle yükle</small></div>
              </div>
              <span className="workflow-arrow">→</span>
              <div className="workflow-step">
                <span className="step-icon">?</span>
                <div><b>Merak ettiğini sor</b><small>Doğal Türkçe ile sorunu yaz</small></div>
              </div>
              <span className="workflow-arrow">→</span>
              <div className="workflow-step">
                <span className="step-icon accent">✓</span>
                <div><b>Kaynaklı cevabı al</b><small>Dayandığı sayfayı birlikte gör</small></div>
              </div>
            </div>

            <div className="trust-row">
              <span>✓ Kaynak dışına çıkmaz</span>
              <span>✓ Türkçe yanıt verir</span>
              <span>✓ Belgelerin sende kalır</span>
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

            <div className="auth-intro">
              <span className="auth-icon">{authMode === 'login' ? '→' : '+'}</span>
              <div>
                <strong>{authMode === 'login' ? 'Çalışma alanına dön' : 'Kendi alanını oluştur'}</strong>
                <p>{authMode === 'login'
                  ? 'Belgelerin ve önceki sohbetlerin seni bekliyor.'
                  : 'Dakikalar içinde kişisel belge asistanını kullanmaya başla.'}</p>
              </div>
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
                {authBusy ? 'İşleniyor…' : authMode === 'login' ? 'Çalışma Alanını Aç' : 'Ücretsiz Hesap Oluştur'}
              </button>
            </form>

            {isDemoMode && (
              <div className="demo-entry">
                <div className="demo-divider"><span>veya</span></div>
                <button className="demo-btn" type="button" onClick={handleOpenDemo}>
                  <span className="demo-btn-icon">▶</span>
                  <span><strong>Canlı demoyu incele</strong><small>Kayıt gerekmez · örnek veriler kullanılır</small></span>
                </button>
              </div>
            )}

            <p className="auth-security"><span>⌁</span> Verilerin üçüncü taraflarla paylaşılmaz.</p>
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
      <h1 className="sr-only">SmartDocs AI çalışma alanı</h1>
      <aside className="sidebar panel">
        <div>
          <div className="brand-pill"><span className="brand-mark">✦</span> SmartDocs AI</div>
          {isDemoMode && <div className="demo-mode-badge"><span /> Vitrin demosu</div>}
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
          <label
            className={`file-dropzone ${isDraggingFile ? 'dragging' : ''}`}
            onDragEnter={(event) => {
              event.preventDefault();
              setIsDraggingFile(true);
            }}
            onDragOver={(event) => event.preventDefault()}
            onDragLeave={(event) => {
              event.preventDefault();
              if (event.currentTarget === event.target) setIsDraggingFile(false);
            }}
            onDrop={(event) => {
              event.preventDefault();
              setIsDraggingFile(false);
              handleFileChange(event.dataTransfer.files?.[0] ?? null);
            }}
          >
            <span className="upload-icon">PDF</span>
            <span>
              <strong>{uploadFile ? uploadFile.name : 'PDF dosyanı seç'}</strong>
              <small>{uploadFile ? formatSize(uploadFile.size) : 'veya buraya sürükleyip bırak'}</small>
            </span>
            <input
              ref={fileInputRef}
              className="file-input-hidden"
              type="file"
              accept="application/pdf,.pdf"
              aria-describedby="upload-help"
              onChange={(event) => handleFileChange(event.target.files?.[0] ?? null)}
            />
          </label>
          <div className="upload-help-row">
            <small id="upload-help" className="muted">En fazla 100 MB · yalnızca PDF</small>
            {uploadFile && (
              <button
                type="button"
                className="text-btn"
                onClick={() => handleFileChange(null)}
              >
                Seçimi kaldır
              </button>
            )}
          </div>
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

        <div className="sidebar-footer">
          {isDemoMode && (
            <a
              className="repo-link"
              href="https://github.com/Nurettin-Erdogan/smartdocs-ai"
              target="_blank"
              rel="noreferrer"
            >
              Kaynak kodu incele <span>↗</span>
            </a>
          )}
          <button type="button" className="ghost-btn danger" onClick={handleLogout}>
            Çıkış Yap
          </button>
        </div>
      </aside>

      <main className="main-grid">
        <section className="panel documents-panel">
          <div className="section-head">
            <div>
              <p className="eyebrow">BELGE KÜTÜPHANEN</p>
              <h3>Dokümanlar</h3>
              <p className="muted">PDF’lerini ekle, durumlarını takip et ve her an ulaş.</p>
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
                {ACTIVE_DOCUMENT_STATUSES.includes(document.indexingStatus) && (
                  <div className={`processing-track processing-${document.indexingStatus.toLowerCase()}`}>
                    <span />
                  </div>
                )}
                {document.indexingError && (
                  <p className="document-error" role="status">{document.indexingError}</p>
                )}
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
                    disabled={anyDocumentAction ||
                      ACTIVE_DOCUMENT_STATUSES.includes(document.indexingStatus)}
                  >
                    {documentAction?.id === document.id && documentAction.kind === 'delete'
                      ? 'Siliniyor…'
                      : document.indexingStatus === 'Deleting'
                        ? 'Temizliği yeniden dene'
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
              <p className="eyebrow">KAYNAKLI YAPAY ZEKÂ</p>
              <h3>Belge Asistanı</h3>
              <p className="muted">Sorunu sor; yanıtın dayandığı kaynakları birlikte incele.</p>
            </div>
            <span className="count-badge">{selectedConversationId ?? 'Yeni'}</span>
          </div>

          <div className="document-scope" aria-label="Cevapta kullanılacak belgeler">
            <div className="scope-head">
              <div>
                <strong>Cevap kapsamı</strong>
                <small>{selectedDocumentIds.length} belge seçili</small>
              </div>
              {readyDocuments.length > 1 && (
                <button
                  type="button"
                  className="text-btn"
                  onClick={() => setSelectedDocumentIds(readyDocuments.map((document) => document.id))}
                >
                  Tümünü seç
                </button>
              )}
            </div>
            <div className="scope-list">
              {readyDocuments.length === 0 && (
                <span className="muted">Sohbete hazır belge bulunmuyor.</span>
              )}
              {readyDocuments.map((document) => {
                const isSelected = selectedDocumentIds.includes(document.id);
                return (
                  <button
                    key={document.id}
                    type="button"
                    className={`scope-chip ${isSelected ? 'selected' : ''}`}
                    aria-pressed={isSelected}
                    title={document.title}
                    onClick={() => setSelectedDocumentIds((currentIds) =>
                      isSelected
                        ? currentIds.filter((id) => id !== document.id)
                        : [...currentIds, document.id])}
                  >
                    <span>{isSelected ? '✓' : '+'}</span>
                    {document.title}
                  </button>
                );
              })}
            </div>
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
              {asking ? (
                <button className="stop-btn" type="button" onClick={handleStopAnswer}>
                  <span className="stop-icon" /> Cevabı Durdur
                </button>
              ) : (
                <button className="primary-btn" type="submit" disabled={!question.trim()}>
                  Soruyu Gönder
                </button>
              )}
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
