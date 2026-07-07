# SmartDocs AI Database Design

## 1. Users

Kullanıcı bilgileri

| Alan | Tip | Açıklama |
|------|-----|----------|
| Id | int | Kullanıcı ID |
| FullName | nvarchar | Ad Soyad |
| Email | nvarchar | Email |
| PasswordHash | nvarchar | Şifre |
| RoleId | int | Kullanıcı Rolü |
| CreatedAt | datetime | Oluşturulma Tarihi |

---

## 2. Roles

Kullanıcı rolleri

| Alan | Tip |
|------|-----|
| Id | int |
| Name | nvarchar |

Roller

- Admin
- Personel
- Misafir

---

## 3. Documents

Yüklenen belgeler

| Alan | Tip |
|------|-----|
| Id | int |
| UserId | int |
| Title | nvarchar |
| FileName | nvarchar |
| FileType | nvarchar |
| FilePath | nvarchar |
| FileSize | bigint |
| UploadDate | datetime |

---

## 4. Chunks

Belgenin parçaları

| Alan | Tip |
|------|-----|
| Id | int |
| DocumentId | int |
| ChunkIndex | int |
| Content | nvarchar(max) |
| PageNumber | int |

---

## 5. Conversations

Sohbetler

| Alan | Tip |
|------|-----|
| Id | int |
| UserId | int |
| CreatedAt | datetime |

---

## 6. Messages

Mesajlar

| Alan | Tip |
|------|-----|
| Id | int |
| ConversationId | int |
| Question | nvarchar(max) |
| Answer | nvarchar(max) |
| CreatedAt | datetime |