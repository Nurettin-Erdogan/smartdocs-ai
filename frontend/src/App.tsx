import { FormEvent, useEffect, useMemo, useState } from 'react';
import { api, ChatConversation, ChatResponse, DocumentItem } from './api';

type AuthMode = 'login' | 'register';

type UserState = {
  fullName: string;
  email: string;
  role?: string;
};

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

function App() {
  const [authMode, setAuthMode] = useState<AuthMode>('login');
  const [user, setUser] = useState<UserState | null>(null);
  const [token, setToken] = useState<string | null>(null);
  const [documents, setDocuments] = useState<DocumentItem[]>([]);
  const [history, setHistory] = useState<ChatConversation[]>([]);
  const [selectedConversationId, setSelectedConversationId] = useState<number | null>(null);
  const [question, setQuestion] = useState('');
  const [answer, setAnswer] = useState('');
  const [sources, setSources] = useState<ChatResponse['sources']>([]);
  const [authForm, setAuthForm] = useState({ fullName: '', email: '', password: '' });
  const [loginForm, setLoginForm] = useState({ email: '', password: '' });
  const [uploadFile, setUploadFile] = useState<File | null>(null);
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState('');
  const [error, setError] = useState('');

  useEffect(() => {
    const storedToken = localStorage.getItem('smartdocs_token');
    const storedUser = localStorage.getItem('smartdocs_user');

    if (storedToken && storedUser) {
      setToken(storedToken);
      setUser(JSON.parse(storedUser) as UserState);
    }
  }, []);

  useEffect(() => {
    if (!token) return;

    void refreshData();
  }, [token]);

  const dashboardStats = useMemo(() => {
    const totalDocs = documents.length;
    const totalConversations = history.length;
    const totalMessages = history.reduce((count, item) => count + item.messages.length, 0);
    const latestDoc = documents[0];

    return [
      { label: 'Toplam doküman', value: String(totalDocs) },
      { label: 'Toplam sohbet', value: String(totalConversations) },
      { label: 'Toplam mesaj', value: String(totalMessages) },
      { label: 'Son yükleme', value: latestDoc ? latestDoc.title : 'Yok' }
    ];
  }, [documents, history]);

  const refreshData = async () => {
    try {
      setError('');
      const [docs, chats] = await Promise.all([api.listDocuments(), api.chatHistory()]);
      setDocuments(docs);
      setHistory(chats);
      if (!selectedConversationId && chats.length > 0) {
        setSelectedConversationId(chats[0].conversationId);
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Veriler yüklenemedi.');
    }
  };

  const persistAuth = (nextToken: string, nextUser: UserState) => {
    localStorage.setItem('smartdocs_token', nextToken);
    localStorage.setItem('smartdocs_user', JSON.stringify(nextUser));
    setToken(nextToken);
    setUser(nextUser);
  };

  const handleLogin = async (event: FormEvent) => {
    event.preventDefault();
    try {
      setBusy(true);
      setError('');
      const result = await api.login(loginForm);
      persistAuth(result.token, {
        fullName: result.fullName,
        email: result.email,
        role: result.role
      });
      setNotice('Giriş başarılı.');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Giriş başarısız oldu.');
    } finally {
      setBusy(false);
    }
  };

  const handleRegister = async (event: FormEvent) => {
    event.preventDefault();
    try {
      setBusy(true);
      setError('');
      const result = await api.register(authForm);
      persistAuth(result.token, {
        fullName: result.fullName,
        email: result.email,
        role: result.role
      });
      setNotice('Kayıt başarılı.');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Kayıt başarısız oldu.');
    } finally {
      setBusy(false);
    }
  };

  const handleUpload = async () => {
    if (!uploadFile) {
      setError('Önce bir PDF seçmelisin.');
      return;
    }

    try {
      setBusy(true);
      setError('');
      const result = await api.uploadDocument(uploadFile);
      setNotice(result.indexingStatus ?? 'Dosya yüklendi.');
      setUploadFile(null);
      await refreshData();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Yükleme başarısız oldu.');
    } finally {
      setBusy(false);
    }
  };

  const handleDelete = async (id: number) => {
    try {
      setBusy(true);
      setError('');
      await api.deleteDocument(id);
      setNotice('Belge silindi.');
      await refreshData();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Silme başarısız oldu.');
    } finally {
      setBusy(false);
    }
  };

  const handleAsk = async (event: FormEvent) => {
    event.preventDefault();
    const trimmedQuestion = question.trim();

    if (!trimmedQuestion) {
      setError('Soru yazmalısın.');
      return;
    }

    try {
      setBusy(true);
      setError('');
      const result = await api.askChat({
        question: trimmedQuestion,
        conversationId: selectedConversationId
      });
      setAnswer(result.answer);
      setSources(result.sources);
      setSelectedConversationId(result.conversationId);
      setQuestion('');
      setNotice('Soru gönderildi.');
      await refreshData();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Soru gönderilemedi.');
    } finally {
      setBusy(false);
    }
  };

  const handleLogout = () => {
    localStorage.removeItem('smartdocs_token');
    localStorage.removeItem('smartdocs_user');
    setToken(null);
    setUser(null);
    setDocuments([]);
    setHistory([]);
    setSelectedConversationId(null);
    setQuestion('');
    setAnswer('');
    setSources([]);
    setNotice('Çıkış yapıldı.');
  };

  const activeConversation = history.find((item) => item.conversationId === selectedConversationId) ?? history[0];

  if (!token || !user) {
    return (
      <div className="auth-shell">
        <div className="auth-backdrop" />
        <main className="auth-card">
          <section className="hero-block">
            <div className="brand-pill">SmartDocs AI</div>
            <h1>PDF belgelerini yükle, sor, kaynakla cevap al.</h1>
            <p>
              Yerel Ollama, Qdrant ve ASP.NET Core tabanlı çalışma alanı için sade ama güçlü bir giriş ekranı.
            </p>
            <div className="feature-row">
              <span>JWT giriş</span>
              <span>PDF yükleme</span>
              <span>Chat geçmişi</span>
            </div>
          </section>

          <section className="panel auth-panel">
            <div className="mode-switch">
              <button className={authMode === 'login' ? 'active' : ''} onClick={() => setAuthMode('login')} type="button">
                Giriş Yap
              </button>
              <button className={authMode === 'register' ? 'active' : ''} onClick={() => setAuthMode('register')} type="button">
                Kayıt Ol
              </button>
            </div>

            <form onSubmit={authMode === 'login' ? handleLogin : handleRegister} className="stack">
              {authMode === 'register' && (
                <label>
                  Ad Soyad
                  <input value={authForm.fullName} onChange={(e) => setAuthForm({ ...authForm, fullName: e.target.value })} placeholder="Ad Soyad" />
                </label>
              )}
              <label>
                E-posta
                <input value={authMode === 'login' ? loginForm.email : authForm.email} onChange={(e) => authMode === 'login' ? setLoginForm({ ...loginForm, email: e.target.value }) : setAuthForm({ ...authForm, email: e.target.value })} type="email" placeholder="ornek@firma.com" />
              </label>
              <label>
                Şifre
                <input value={authMode === 'login' ? loginForm.password : authForm.password} onChange={(e) => authMode === 'login' ? setLoginForm({ ...loginForm, password: e.target.value }) : setAuthForm({ ...authForm, password: e.target.value })} type="password" placeholder="••••••••" />
              </label>
              <button disabled={busy} className="primary-btn" type="submit">
                {busy ? 'İşleniyor...' : authMode === 'login' ? 'Giriş Yap' : 'Hesap Oluştur'}
              </button>
            </form>

            {(error || notice) && <div className={error ? 'feedback error' : 'feedback success'}>{error || notice}</div>}
          </section>
        </main>
      </div>
    );
  }

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
              <strong>{item.value}</strong>
            </article>
          ))}
        </div>

        <div className="panel-subsection">
          <div className="section-head">
            <h3>Belge yükle</h3>
            <button type="button" className="ghost-btn" onClick={refreshData}>Yenile</button>
          </div>
          <input type="file" accept="application/pdf" onChange={(e) => setUploadFile(e.target.files?.[0] ?? null)} />
          <button type="button" className="primary-btn" onClick={handleUpload} disabled={busy}>
            PDF Yükle
          </button>
        </div>

        <div className="panel-subsection history-list">
          <div className="section-head">
            <h3>Sohbet geçmişi</h3>
            <span>{history.length}</span>
          </div>
          <div className="scroll-list">
            {history.length === 0 && <p className="muted">Henüz sohbet yok.</p>}
            {history.map((conversation) => (
              <button
                key={conversation.conversationId}
                type="button"
                className={`history-item ${selectedConversationId === conversation.conversationId ? 'active' : ''}`}
                onClick={() => setSelectedConversationId(conversation.conversationId)}
              >
                <strong>Sohbet #{conversation.conversationId}</strong>
                <span>{formatDate(conversation.createdAt)}</span>
                <small>{conversation.messages.length} mesaj</small>
              </button>
            ))}
          </div>
        </div>

        <button type="button" className="ghost-btn danger" onClick={handleLogout}>Çıkış Yap</button>
      </aside>

      <main className="main-grid">
        <section className="panel documents-panel">
          <div className="section-head">
            <div>
              <h3>Dokümanlar</h3>
              <p className="muted">Yüklenen PDF’ler ve işlem durumu.</p>
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
                  <span>{document.fileType}</span>
                  <span>{formatSize(document.fileSize)}</span>
                  <span>{formatDate(document.uploadDate)}</span>
                </div>
                <button type="button" className="ghost-btn danger" onClick={() => handleDelete(document.id)} disabled={busy}>
                  Sil
                </button>
              </article>
            ))}
          </div>
        </section>

        <section className="panel chat-panel">
          <div className="section-head">
            <div>
              <h3>AI Chat</h3>
              <p className="muted">Soru sor, ilgili PDF parçalarından cevap al.</p>
            </div>
            <span className="count-badge">{selectedConversationId ?? 'Yeni'}</span>
          </div>

          <form onSubmit={handleAsk} className="chat-form">
            <textarea value={question} onChange={(e) => setQuestion(e.target.value)} placeholder="Örn: Bu dokümanda iade süresi kaç gün?" rows={5} />
            <button className="primary-btn" type="submit" disabled={busy}>
              {busy ? 'Cevap hazırlanıyor...' : 'Soruyu Gönder'}
            </button>
          </form>

          <div className="answer-box">
            <div className="section-head compact">
              <h4>Cevap</h4>
              <span>{activeConversation ? `Sohbet #${activeConversation.conversationId}` : 'Bekleniyor'}</span>
            </div>
            <p>{answer || 'Henüz bir soru sorulmadı.'}</p>
          </div>

          <div className="sources-box">
            <div className="section-head compact">
              <h4>Kaynaklar</h4>
              <span>{sources.length}</span>
            </div>
            {sources.length === 0 && <p className="muted">Cevap verildiğinde burada ilgili parça listesi görünür.</p>}
            <div className="source-list">
              {sources.map((source) => (
                <article key={`${source.documentId}-${source.chunkIndex}-${source.pageNumber}`} className="source-card">
                  <strong>Belge {source.documentId} · Sayfa {source.pageNumber}</strong>
                  <p>{source.content}</p>
                  <small>Parça {source.chunkIndex} · Skor {source.score.toFixed(3)}</small>
                </article>
              ))}
            </div>
          </div>
        </section>
      </main>

      {notice && <div className="toast success">{notice}</div>}
      {error && <div className="toast error">{error}</div>}
    </div>
  );
}

export default App;
