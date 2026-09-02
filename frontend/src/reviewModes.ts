export type ReviewMode = {
  id: 'summary' | 'facts' | 'risks' | 'compare';
  icon: string;
  title: string;
  description: string;
  prompt: string;
};

const SINGLE_DOCUMENT_MODES: ReviewMode[] = [
  {
    id: 'summary',
    icon: '01',
    title: 'Yönetici özeti',
    description: 'Belgenin karar vermek için gerekli kısmını çıkarır.',
    prompt: 'Seçili belgeyi karar verici için en fazla 5 maddede özetle. Her maddeyi belge içindeki sayfa kanıtıyla destekle; belgede olmayan bilgi ekleme.'
  },
  {
    id: 'facts',
    icon: '02',
    title: 'Kritik bilgiler',
    description: 'İsim, kurum, tarih, süre ve tutarları ayırır.',
    prompt: 'Seçili belgedeki kişi, kurum, tarih, süre, tutar ve iletişim bilgilerini kategorilere ayırarak çıkar. Bulunmayan kategorileri uydurma ve her bulgu için kaynak sayfasını belirt.'
  },
  {
    id: 'risks',
    icon: '03',
    title: 'Risk taraması',
    description: 'Yükümlülükleri, belirsizlikleri ve son tarihleri bulur.',
    prompt: 'Seçili belgedeki yükümlülükleri, son tarihleri, belirsizlikleri ve risk oluşturabilecek noktaları önem sırasıyla listele. Her bulguyu doğrudan belge kanıtıyla destekle; kanıt yoksa açıkça belirt.'
  }
];

const COMPARE_MODE: ReviewMode = {
  id: 'compare',
  icon: '04',
  title: 'Tutarsızlık bul',
  description: 'Seçili belgelerdeki çelişen bilgileri karşılaştırır.',
  prompt: 'Seçili belgeler arasında birbiriyle çelişen veya farklılaşan tarih, tutar, kişi, yükümlülük ve ifadeleri karşılaştır. Her farkı iki tarafın sayfa kanıtıyla göster; yeterli kanıt yoksa açıkça belirt.'
};

export const reviewModesFor = (selectedDocumentCount: number): ReviewMode[] =>
  selectedDocumentCount > 1
    ? [...SINGLE_DOCUMENT_MODES, COMPARE_MODE]
    : [...SINGLE_DOCUMENT_MODES];
