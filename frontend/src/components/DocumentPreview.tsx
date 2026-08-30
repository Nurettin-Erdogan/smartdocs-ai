import { useEffect, useRef, useState } from 'react';
import type { PDFDocumentProxy, RenderTask } from 'pdfjs-dist';
import { api } from '../api';

export type PreviewTarget = {
  documentId: number;
  title: string;
  pageNumber: number;
  content?: string;
};

type DocumentPreviewProps = {
  target: PreviewTarget;
  onClose: () => void;
};

export function DocumentPreview({ target, onClose }: DocumentPreviewProps) {
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const pdfRef = useRef<PDFDocumentProxy | null>(null);
  const renderTaskRef = useRef<RenderTask | null>(null);
  const [pageNumber, setPageNumber] = useState(Math.max(1, target.pageNumber));
  const [pageCount, setPageCount] = useState(0);
  const [status, setStatus] = useState<'loading' | 'ready' | 'demo' | 'error'>('loading');
  const [error, setError] = useState('');

  useEffect(() => {
    const controller = new AbortController();
    let disposed = false;
    let loadingTask: ReturnType<(typeof import('pdfjs-dist'))['getDocument']> | null = null;

    const load = async () => {
      setStatus('loading');
      setError('');
      try {
        const file = await api.getDocumentFile(target.documentId, controller.signal);
        if (disposed) return;
        if (!file) {
          setStatus('demo');
          return;
        }

        const [pdfjs, workerModule] = await Promise.all([
          import('pdfjs-dist'),
          import('pdfjs-dist/build/pdf.worker.min.mjs?url')
        ]);
        pdfjs.GlobalWorkerOptions.workerSrc = workerModule.default;
        loadingTask = pdfjs.getDocument({ data: new Uint8Array(await file.arrayBuffer()) });
        const pdf = await loadingTask.promise;
        if (disposed) {
          await loadingTask.destroy();
          return;
        }

        pdfRef.current = pdf;
        setPageCount(pdf.numPages);
        setPageNumber(Math.min(Math.max(1, target.pageNumber), pdf.numPages));
        setStatus('ready');
      } catch (loadError) {
        if (disposed || (loadError instanceof DOMException && loadError.name === 'AbortError')) return;
        setError(loadError instanceof Error ? loadError.message : 'PDF görüntülenemedi.');
        setStatus('error');
      }
    };

    void load();
    return () => {
      disposed = true;
      controller.abort();
      renderTaskRef.current?.cancel();
      void loadingTask?.destroy();
      pdfRef.current = null;
    };
  }, [target.documentId, target.pageNumber]);

  useEffect(() => {
    const pdf = pdfRef.current;
    const canvas = canvasRef.current;
    if (status !== 'ready' || !pdf || !canvas) return;

    let disposed = false;
    const render = async () => {
      const page = await pdf.getPage(pageNumber);
      try {
        if (disposed) return;
        const viewport = page.getViewport({ scale: 1.45 });
        const pixelRatio = Math.min(globalThis.devicePixelRatio || 1, 2);
        canvas.width = Math.floor(viewport.width * pixelRatio);
        canvas.height = Math.floor(viewport.height * pixelRatio);
        canvas.style.aspectRatio = `${viewport.width} / ${viewport.height}`;
        const context = canvas.getContext('2d');
        if (!context) throw new Error('PDF çizim alanı hazırlanamadı.');

        const task = page.render({
          canvas,
          canvasContext: context,
          viewport,
          transform: pixelRatio === 1 ? undefined : [pixelRatio, 0, 0, pixelRatio, 0, 0]
        });
        renderTaskRef.current = task;
        await task.promise;
      } catch (renderError) {
        if (!disposed && !(renderError instanceof Error && renderError.name === 'RenderingCancelledException')) {
          setError('PDF sayfası çizilemedi.');
          setStatus('error');
        }
      } finally {
        page.cleanup();
      }
    };

    void render();
    return () => {
      disposed = true;
      renderTaskRef.current?.cancel();
    };
  }, [pageNumber, status]);

  useEffect(() => {
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onClose();
    };
    document.addEventListener('keydown', handleKeyDown);
    return () => document.removeEventListener('keydown', handleKeyDown);
  }, [onClose]);

  return (
    <div className="preview-backdrop" role="presentation" onMouseDown={(event) => {
      if (event.target === event.currentTarget) onClose();
    }}>
      <section className="preview-dialog" role="dialog" aria-modal="true" aria-labelledby="preview-title">
        <header className="preview-header">
          <div>
            <p className="eyebrow">KAYNAK DOĞRULAMA</p>
            <h3 id="preview-title">{target.title}</h3>
          </div>
          <button type="button" className="preview-close" onClick={onClose} aria-label="Önizlemeyi kapat">×</button>
        </header>

        <div className="preview-toolbar">
          <button type="button" onClick={() => setPageNumber((page) => Math.max(1, page - 1))}
            disabled={status !== 'ready' || pageNumber <= 1}>← Önceki</button>
          <strong>Sayfa {pageNumber}{pageCount > 0 ? ` / ${pageCount}` : ''}</strong>
          <button type="button" onClick={() => setPageNumber((page) => Math.min(pageCount, page + 1))}
            disabled={status !== 'ready' || pageNumber >= pageCount}>Sonraki →</button>
        </div>

        <div className="preview-content">
          <div className="pdf-stage" aria-live="polite">
            {status === 'loading' && <div className="preview-state"><span className="preview-spinner" />PDF hazırlanıyor…</div>}
            {status === 'error' && <div className="preview-state error">{error}</div>}
            {status === 'demo' && (
              <div className="demo-page-placeholder">
                <span>PDF</span>
                <h4>{target.title}</h4>
                <p>Canlı demoda kaynak sayfa deneyimi bu alanda gösterilir.</p>
                <i /><i /><i /><i />
              </div>
            )}
            <canvas ref={canvasRef} hidden={status !== 'ready'} aria-label={`PDF sayfa ${pageNumber}`} />
          </div>

          {target.content && (
            <aside className="source-highlight">
              <span>Yanıtta kullanılan bölüm</span>
              <mark>{target.content}</mark>
              <small>Bu metin PDF’nin {target.pageNumber}. sayfasından getirildi.</small>
            </aside>
          )}
        </div>
      </section>
    </div>
  );
}
