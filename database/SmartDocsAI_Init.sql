IF DB_ID(N'SmartDocsAI_Db') IS NULL
BEGIN
    CREATE DATABASE SmartDocsAI_Db;
END
GO

USE SmartDocsAI_Db;
GO

IF OBJECT_ID(N'dbo.Messages', N'U') IS NOT NULL DROP TABLE dbo.Messages;
IF OBJECT_ID(N'dbo.Conversations', N'U') IS NOT NULL DROP TABLE dbo.Conversations;
IF OBJECT_ID(N'dbo.Chunks', N'U') IS NOT NULL DROP TABLE dbo.Chunks;
IF OBJECT_ID(N'dbo.Documents', N'U') IS NOT NULL DROP TABLE dbo.Documents;
IF OBJECT_ID(N'dbo.Users', N'U') IS NOT NULL DROP TABLE dbo.Users;
IF OBJECT_ID(N'dbo.Roles', N'U') IS NOT NULL DROP TABLE dbo.Roles;
GO

CREATE TABLE dbo.Roles
(
    Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Roles PRIMARY KEY,
    Name NVARCHAR(50) NOT NULL
);
GO

CREATE UNIQUE INDEX IX_Roles_Name ON dbo.Roles(Name);
GO

CREATE TABLE dbo.Users
(
    Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Users PRIMARY KEY,
    FullName NVARCHAR(100) NOT NULL,
    Email NVARCHAR(150) NOT NULL,
    PasswordHash NVARCHAR(MAX) NOT NULL,
    RoleId INT NOT NULL,
    CreatedAt DATETIME2 NOT NULL
);
GO

CREATE UNIQUE INDEX IX_Users_Email ON dbo.Users(Email);
GO

CREATE TABLE dbo.Documents
(
    Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Documents PRIMARY KEY,
    UserId INT NOT NULL,
    Title NVARCHAR(255) NOT NULL,
    FileName NVARCHAR(255) NOT NULL,
    FileType NVARCHAR(50) NOT NULL,
    FilePath NVARCHAR(MAX) NOT NULL,
    FileSize BIGINT NOT NULL,
    UploadDate DATETIME2 NOT NULL
);
GO

CREATE INDEX IX_Documents_UserId ON dbo.Documents(UserId);
GO

CREATE TABLE dbo.Chunks
(
    Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Chunks PRIMARY KEY,
    DocumentId INT NOT NULL,
    ChunkIndex INT NOT NULL,
    Content NVARCHAR(MAX) NOT NULL,
    PageNumber INT NOT NULL
);
GO

CREATE INDEX IX_Chunks_DocumentId ON dbo.Chunks(DocumentId);
GO

CREATE TABLE dbo.Conversations
(
    Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Conversations PRIMARY KEY,
    UserId INT NOT NULL,
    CreatedAt DATETIME2 NOT NULL
);
GO

CREATE INDEX IX_Conversations_UserId ON dbo.Conversations(UserId);
GO

CREATE TABLE dbo.Messages
(
    Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Messages PRIMARY KEY,
    ConversationId INT NOT NULL,
    Question NVARCHAR(MAX) NOT NULL,
    Answer NVARCHAR(MAX) NOT NULL,
    CreatedAt DATETIME2 NOT NULL
);
GO

CREATE INDEX IX_Messages_ConversationId ON dbo.Messages(ConversationId);
GO

ALTER TABLE dbo.Users
ADD CONSTRAINT FK_Users_Roles_RoleId
FOREIGN KEY (RoleId) REFERENCES dbo.Roles(Id)
ON DELETE NO ACTION;
GO

ALTER TABLE dbo.Documents
ADD CONSTRAINT FK_Documents_Users_UserId
FOREIGN KEY (UserId) REFERENCES dbo.Users(Id)
ON DELETE CASCADE;
GO

ALTER TABLE dbo.Chunks
ADD CONSTRAINT FK_Chunks_Documents_DocumentId
FOREIGN KEY (DocumentId) REFERENCES dbo.Documents(Id)
ON DELETE CASCADE;
GO

ALTER TABLE dbo.Conversations
ADD CONSTRAINT FK_Conversations_Users_UserId
FOREIGN KEY (UserId) REFERENCES dbo.Users(Id)
ON DELETE CASCADE;
GO

ALTER TABLE dbo.Messages
ADD CONSTRAINT FK_Messages_Conversations_ConversationId
FOREIGN KEY (ConversationId) REFERENCES dbo.Conversations(Id)
ON DELETE CASCADE;
GO

INSERT INTO dbo.Roles (Id, Name)
VALUES (1, N'Admin'), (2, N'Personel'), (3, N'Misafir');
GO
