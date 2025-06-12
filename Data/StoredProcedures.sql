USE master;
GO

-- Create the database if it doesn't exist
IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = 'PDFCommenterDB')
BEGIN
    CREATE DATABASE PDFCommenterDB;
END
GO

USE PDFCommenterDB;
GO

-- Drop UploadTime column if it exists
IF EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('UploadedFiles') AND name = 'UploadTime')
BEGIN
    -- First drop the default constraint
    DECLARE @ConstraintName nvarchar(200)
    SELECT @ConstraintName = dc.name
    FROM sys.default_constraints dc
    JOIN sys.columns c ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
    WHERE c.object_id = OBJECT_ID('UploadedFiles') AND c.name = 'UploadTime'

    IF @ConstraintName IS NOT NULL
    BEGIN
        EXEC('ALTER TABLE UploadedFiles DROP CONSTRAINT ' + @ConstraintName)
    END

    -- Then drop the column
    ALTER TABLE UploadedFiles DROP COLUMN UploadTime
END
GO

-- Create UploadedFiles table if it doesn't exist
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'UploadedFiles')
BEGIN
    CREATE TABLE UploadedFiles (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        FileName NVARCHAR(255) NOT NULL,
        FilePath NVARCHAR(500) NOT NULL,
        FileType NVARCHAR(10) NOT NULL,
        FileSizeMB FLOAT NOT NULL,
        PageCount INT NULL,
        UploadedBy NVARCHAR(100) NOT NULL,
        Status NVARCHAR(20) NOT NULL DEFAULT 'Pending'
    );
END
GO

-- Create Comments table if it doesn't exist
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Comments')
BEGIN
    CREATE TABLE Comments (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        FileId INT NOT NULL,
        PageNumber INT NOT NULL,
        XPosition FLOAT NOT NULL,
        YPosition FLOAT NOT NULL,
        CommentText NVARCHAR(MAX) NOT NULL,
        CreatedBy NVARCHAR(100) NOT NULL,
        CreatedAt DATETIME NOT NULL DEFAULT GETDATE(),
        CONSTRAINT FK_Comments_UploadedFiles FOREIGN KEY (FileId) 
            REFERENCES UploadedFiles(Id) ON DELETE CASCADE
    );
END
GO

-- Drop stored procedures
IF EXISTS (SELECT * FROM sys.procedures WHERE name = 'InsertUploadedFile')
BEGIN
    DROP PROCEDURE InsertUploadedFile;
END
GO

-- Recreate stored procedures
CREATE PROCEDURE InsertUploadedFile
    @FileName NVARCHAR(255),
    @FilePath NVARCHAR(500),
    @FileType NVARCHAR(10),
    @FileSizeMB FLOAT,
    @PageCount INT = NULL,
    @UploadedBy NVARCHAR(100),
    @Status NVARCHAR(20)
AS
BEGIN
    INSERT INTO UploadedFiles (
        FileName, 
        FilePath, 
        FileType, 
        FileSizeMB, 
        PageCount, 
        UploadedBy, 
        Status
    )
    VALUES (
        @FileName, 
        @FilePath, 
        @FileType, 
        @FileSizeMB, 
        @PageCount, 
        @UploadedBy, 
        @Status
    );
    
    SELECT SCOPE_IDENTITY();
END
GO

IF EXISTS (SELECT * FROM sys.procedures WHERE name = 'GetAllUploadedFiles')
    DROP PROCEDURE GetAllUploadedFiles;
GO

CREATE PROCEDURE GetAllUploadedFiles
AS
BEGIN
    SELECT 
        Id,
        FileName,
        FilePath,
        FileType,
        FileSizeMB,
        PageCount,
        UploadedBy,
        Status
    FROM UploadedFiles 
    ORDER BY Id DESC;
END
GO

IF EXISTS (SELECT * FROM sys.procedures WHERE name = 'GetUploadedFileById')
    DROP PROCEDURE GetUploadedFileById;
GO

CREATE PROCEDURE GetUploadedFileById
    @Id INT
AS
BEGIN
    SELECT 
        Id,
        FileName,
        FilePath,
        FileType,
        FileSizeMB,
        PageCount,
        UploadedBy,
        Status
    FROM UploadedFiles 
    WHERE Id = @Id;
END
GO

IF EXISTS (SELECT * FROM sys.procedures WHERE name = 'InsertComment')
    DROP PROCEDURE InsertComment;
GO

CREATE PROCEDURE InsertComment
    @DocumentId INT,
    @PageNumber INT,
    @XPosition FLOAT,
    @YPosition FLOAT,
    @CommentText NVARCHAR(MAX),
    @CreatedBy NVARCHAR(100)
AS
BEGIN
    INSERT INTO Comments (FileId, PageNumber, XPosition, YPosition, CommentText, CreatedBy)
    VALUES (@DocumentId, @PageNumber, @XPosition, @YPosition, @CommentText, @CreatedBy);
    
    SELECT SCOPE_IDENTITY();
END
GO

IF EXISTS (SELECT * FROM sys.procedures WHERE name = 'UpdateUploadedFileStatus')
    DROP PROCEDURE UpdateUploadedFileStatus;
GO

CREATE PROCEDURE UpdateUploadedFileStatus
    @Id INT,
    @Status NVARCHAR(20)
AS
BEGIN
    UPDATE UploadedFiles SET Status = @Status WHERE Id = @Id;
END
GO

IF EXISTS (SELECT * FROM sys.procedures WHERE name = 'GetPendingUploadedFiles')
    DROP PROCEDURE GetPendingUploadedFiles;
GO

CREATE PROCEDURE GetPendingUploadedFiles
AS
BEGIN
    SELECT * FROM UploadedFiles WHERE Status = 'Pending' ORDER BY Id ASC;
END
GO

SELECT 
    f.Id,
    f.FileName,
    f.FilePath,
    f.FileType,
    f.FileSizeMB,
    f.PageCount,
    f.UploadedBy,
    f.Status,
    ISNULL(c.CommentCount, 0) AS CommentCount
FROM UploadedFiles f
LEFT JOIN (
    SELECT FileId, COUNT(*) AS CommentCount
    FROM Comments
    GROUP BY FileId
) c ON f.Id = c.FileId
ORDER BY f.UploadedBy DESC 