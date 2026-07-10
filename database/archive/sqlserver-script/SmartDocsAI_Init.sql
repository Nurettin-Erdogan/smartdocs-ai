/*
   SmartDocs AI - EF Core migration script

   This script is idempotent: it creates only the migrations that have not
   already been applied. It never drops existing application tables or data.
*/
IF DB_ID(N'SmartDocsAI_Db') IS NULL
BEGIN
    CREATE DATABASE [SmartDocsAI_Db];
END;
GO

USE [SmartDocsAI_Db];
GO
IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260707082724_InitialCreate'
)
BEGIN
    CREATE TABLE [Roles] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(50) NOT NULL,
        CONSTRAINT [PK_Roles] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260707082724_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Roles_Name] ON [Roles] ([Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260707082724_InitialCreate'
)
BEGIN
    CREATE TABLE [Users] (
        [Id] int NOT NULL IDENTITY,
        [FullName] nvarchar(100) NOT NULL,
        [Email] nvarchar(150) NOT NULL,
        [PasswordHash] nvarchar(max) NOT NULL,
        [RoleId] int NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Users] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Users_Roles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [Roles] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260707082724_InitialCreate'
)
BEGIN
    CREATE TABLE [Conversations] (
        [Id] int NOT NULL IDENTITY,
        [UserId] int NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Conversations] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Conversations_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260707082724_InitialCreate'
)
BEGIN
    CREATE TABLE [Documents] (
        [Id] int NOT NULL IDENTITY,
        [UserId] int NOT NULL,
        [Title] nvarchar(255) NOT NULL,
        [FileName] nvarchar(255) NOT NULL,
        [FileType] nvarchar(50) NOT NULL,
        [FilePath] nvarchar(max) NOT NULL,
        [FileSize] bigint NOT NULL,
        [UploadDate] datetime2 NOT NULL,
        CONSTRAINT [PK_Documents] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Documents_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260707082724_InitialCreate'
)
BEGIN
    CREATE TABLE [Messages] (
        [Id] int NOT NULL IDENTITY,
        [ConversationId] int NOT NULL,
        [Question] nvarchar(max) NOT NULL,
        [Answer] nvarchar(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Messages] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Messages_Conversations_ConversationId] FOREIGN KEY ([ConversationId]) REFERENCES [Conversations] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260707082724_InitialCreate'
)
BEGIN
    CREATE TABLE [Chunks] (
        [Id] int NOT NULL IDENTITY,
        [DocumentId] int NOT NULL,
        [ChunkIndex] int NOT NULL,
        [Content] nvarchar(max) NOT NULL,
        [PageNumber] int NOT NULL,
        CONSTRAINT [PK_Chunks] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Chunks_Documents_DocumentId] FOREIGN KEY ([DocumentId]) REFERENCES [Documents] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260707082724_InitialCreate'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Name') AND [object_id] = OBJECT_ID(N'[Roles]'))
        SET IDENTITY_INSERT [Roles] ON;
    EXEC(N'INSERT INTO [Roles] ([Id], [Name])
    VALUES (1, N''Admin''),
    (2, N''Personel''),
    (3, N''Misafir'')');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Name') AND [object_id] = OBJECT_ID(N'[Roles]'))
        SET IDENTITY_INSERT [Roles] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260707082724_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Chunks_DocumentId] ON [Chunks] ([DocumentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260707082724_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Conversations_UserId] ON [Conversations] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260707082724_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Documents_UserId] ON [Documents] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260707082724_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Users_Email] ON [Users] ([Email]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260707082724_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Messages_ConversationId] ON [Messages] ([ConversationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260707082724_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Users_RoleId] ON [Users] ([RoleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260707082724_InitialCreate'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260707082724_InitialCreate', N'10.0.9');
END;

COMMIT;
GO
BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260710050145_ImproveDatabaseIndexes'
)
BEGIN
    DROP INDEX [IX_Messages_ConversationId] ON [Messages];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260710050145_ImproveDatabaseIndexes'
)
BEGIN
    DROP INDEX [IX_Documents_UserId] ON [Documents];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260710050145_ImproveDatabaseIndexes'
)
BEGIN
    DROP INDEX [IX_Conversations_UserId] ON [Conversations];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260710050145_ImproveDatabaseIndexes'
)
BEGIN
    DROP INDEX [IX_Chunks_DocumentId] ON [Chunks];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260710050145_ImproveDatabaseIndexes'
)
BEGIN
    CREATE INDEX [IX_Messages_ConversationId_CreatedAt] ON [Messages] ([ConversationId], [CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260710050145_ImproveDatabaseIndexes'
)
BEGIN
    CREATE INDEX [IX_Documents_UserId_UploadDate] ON [Documents] ([UserId], [UploadDate]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260710050145_ImproveDatabaseIndexes'
)
BEGIN
    CREATE INDEX [IX_Conversations_UserId_CreatedAt] ON [Conversations] ([UserId], [CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260710050145_ImproveDatabaseIndexes'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Chunks_DocumentId_ChunkIndex] ON [Chunks] ([DocumentId], [ChunkIndex]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260710050145_ImproveDatabaseIndexes'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260710050145_ImproveDatabaseIndexes', N'10.0.9');
END;

COMMIT;
GO
