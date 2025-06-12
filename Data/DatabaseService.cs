using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Data.SqlClient;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using PDFCommenter.Models;
using System.IO;
using System.Linq;

namespace PDFCommenter.Data
{
    public class DatabaseService
    {
        private readonly string _connectionString;

        public DatabaseService(IConfiguration configuration)
        {
            // Get the connection string from configuration
            _connectionString = configuration.GetConnectionString("DefaultConnection");
            if (string.IsNullOrEmpty(_connectionString))
            {
                throw new InvalidOperationException("Connection string 'DefaultConnection' not found in configuration.");
            }
            
            // Initialize database synchronously to ensure it's ready
            InitializeDatabase();
        }

        private void InitializeDatabase()
        {
            try
            {
                // First, create the database if it doesn't exist
                var builder = new SqlConnectionStringBuilder(_connectionString);
                var databaseName = builder.InitialCatalog;
                builder.InitialCatalog = "master";

                using (var connection = new SqlConnection(builder.ConnectionString))
                {
                    connection.Open();
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = $@"
                            IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = '{databaseName}')
                            BEGIN
                                CREATE DATABASE [{databaseName}];
                            END";
                        command.ExecuteNonQuery();
                    }
                }

                // Now ensure the tables exist with the correct structure
                using (var connection = new SqlConnection(_connectionString))
                {
                    connection.Open();

                    // Check if documents1 table exists and has the required structure
                    bool tableExists = false;
                    bool hasAllColumns = false;
                    
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = "SELECT COUNT(*) FROM sys.tables WHERE name = 'documents1'";
                        tableExists = Convert.ToInt32(command.ExecuteScalar()) > 0;
                    }

                    if (tableExists)
                    {
                        // Check if all required columns exist
                        using (var command = connection.CreateCommand())
                        {
                            command.CommandText = @"
                                SELECT COUNT(*) FROM sys.columns 
                                WHERE object_id = OBJECT_ID('documents1') 
                                AND name IN ('Id', 'FileName', 'FilePath', 'FileType', 'FileSizeMB', 'PageCount', 'UploadedBy', 'Status', 'ExcelName', 'ExcelPath', 'ExcelSizeMB')";
                            hasAllColumns = Convert.ToInt32(command.ExecuteScalar()) >= 11;
                        }
                    }

                    // If table doesn't exist or is missing columns, recreate it
                    if (!tableExists || !hasAllColumns)
                    {
                        // Backup existing data if table exists
                        if (tableExists)
                        {
                            try
                            {
                                using (var command = connection.CreateCommand())
                                {
                                    command.CommandText = @"
                                        IF EXISTS (SELECT * FROM sys.tables WHERE name = 'documents1_backup')
                                            DROP TABLE documents1_backup;
                                        
                                        SELECT * INTO documents1_backup FROM documents1;";
                                    command.ExecuteNonQuery();
                                }
                            }
                            catch
                            {
                                // If backup fails, continue anyway
                            }
                        }

                        // Drop existing table if it exists
                        if (tableExists)
                        {
                            using (var command = connection.CreateCommand())
                            {
                                command.CommandText = "DROP TABLE documents1;";
                                command.ExecuteNonQuery();
                            }
                        }

                        // Create the table with ALL required columns
                        using (var command = connection.CreateCommand())
                        {
                            command.CommandText = @"
                                CREATE TABLE documents1 (
                                    Id INT IDENTITY(1,1) PRIMARY KEY,
                                    FileName NVARCHAR(255) NOT NULL,
                                    FilePath NVARCHAR(500) NOT NULL,
                                    FileType NVARCHAR(10) NOT NULL,
                                    FileSizeMB FLOAT NOT NULL,
                                    PageCount INT NULL,
                                    UploadedBy NVARCHAR(100) NOT NULL,
                                    Status NVARCHAR(20) NOT NULL DEFAULT 'Pending',
                                    ExcelName NVARCHAR(255) NOT NULL,
                                    ExcelPath NVARCHAR(500) NOT NULL,
                                    ExcelSizeMB FLOAT NOT NULL
                                )";
                            command.ExecuteNonQuery();
                        }

                        // Restore data if backup exists
                        if (tableExists)
                        {
                            try
                            {
                                using (var command = connection.CreateCommand())
                                {
                                    command.CommandText = @"
                                        IF EXISTS (SELECT * FROM sys.tables WHERE name = 'documents1_backup')
                                        BEGIN
                                            INSERT INTO documents1 (FileName, FilePath, FileType, FileSizeMB, PageCount, UploadedBy, Status, ExcelName, ExcelPath, ExcelSizeMB)
                                            SELECT 
                                                FileName, 
                                                FilePath, 
                                                FileType, 
                                                FileSizeMB, 
                                                ISNULL(PageCount, NULL), 
                                                UploadedBy, 
                                                ISNULL(Status, 'Pending'),
                                                 ExcelName,
                                                 ExcelPath,
                                                ExcelSizeMB
                                            FROM documents1_backup;
                                        END";
                                    command.ExecuteNonQuery();
                                }
                            }
                            catch
                            {
                                // If restore fails, continue anyway - we have a fresh table
                            }
                        }
                    }

                    // Create Comments table if it doesn't exist
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = @"
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
                                    CONSTRAINT FK_Comments_documents1 FOREIGN KEY (FileId) 
                                        REFERENCES documents1(Id) ON DELETE CASCADE
                                )
                            END";
                        command.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to initialize database: {ex.Message}. Please ensure SQL Server is running and accessible.", ex);
            }
        }

        public async Task<int> CreateDocumentAsync(Document document)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO documents1 (FileName, FilePath, FileType, FileSizeMB, PageCount, UploadedBy, Status, ExcelName, ExcelPath, ExcelSizeMB)
                VALUES (@FileName, @FilePath, @FileType, @FileSizeMB, @PageCount, @UploadedBy, @Status, @ExcelName, @ExcelPath, @ExcelSizeMB);
                SELECT SCOPE_IDENTITY();";

            command.Parameters.AddWithValue("@FileName", document.PDFName);
            command.Parameters.AddWithValue("@FilePath", document.PDFPath);
            
            // Determine file type based on whether Excel properties are set
            string fileType = !string.IsNullOrEmpty(document.ExcelName) ? 
                Path.GetExtension(document.ExcelName).TrimStart('.') : 
                Path.GetExtension(document.PDFName).TrimStart('.');
            command.Parameters.AddWithValue("@FileType", fileType);
            
            command.Parameters.AddWithValue("@FileSizeMB", document.FileSizeMB);
            command.Parameters.AddWithValue("@PageCount", (object)document.PDFPages ?? DBNull.Value);
            command.Parameters.AddWithValue("@UploadedBy", document.UploadedBy);
            command.Parameters.AddWithValue("@Status", document.Status);
            command.Parameters.AddWithValue("@ExcelName", document.ExcelName);
            command.Parameters.AddWithValue("@ExcelPath", document.ExcelPath);
            command.Parameters.AddWithValue("@ExcelSizeMB", document.ExcelSizeMB);

            var result = await command.ExecuteScalarAsync();
            return Convert.ToInt32(result);
        }

        public async Task<List<Document>> GetDocumentsAsync(string filter = null)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                using var command = connection.CreateCommand();
                
                // Use the exact column names that exist in the database
                string sql = @"
                    SELECT 
                        Id,
                        FileName,
                        FilePath,
                        FileType,
                        FileSizeMB,
                        PageCount,
                        UploadedBy,
                        Status,
                        ExcelName,
                        ExcelPath,
                        ExcelSizeMB
                    FROM documents1";
                
                if (!string.IsNullOrEmpty(filter))
                {
                    if (filter == "LastWeek")
                    {
                        sql += " WHERE UploadedBy = @User";
                        command.Parameters.AddWithValue("@User", "admin");
                    }
                    else if (filter == "My")
                    {
                        sql += " WHERE UploadedBy = @User";
                        command.Parameters.AddWithValue("@User", "admin");
                    }
                }
                
                sql += " ORDER BY Id DESC";
                command.CommandText = sql;

                using var reader = await command.ExecuteReaderAsync();
                var documents = new List<Document>();

                while (await reader.ReadAsync())
                {
                    documents.Add(new Document
                    {
                        Id = reader.GetInt32("Id"),
                        PDFName = reader.GetString("FileName"),
                        PDFPath = reader.GetString("FilePath"),
                        PDFPages = reader.IsDBNull("PageCount") ? null : reader.GetInt32("PageCount"),
                        UploadedBy = reader.GetString("UploadedBy"),
                        FileSizeMB = reader.IsDBNull("FileSizeMB") ? 0 : (float)reader.GetDouble("FileSizeMB"),
                        Status = reader.GetString("Status"),
                        ExcelName =  reader.GetString("ExcelName"),
                        ExcelPath =  reader.GetString("ExcelPath"),
                        ExcelSizeMB = reader.String("ExcelSizeMB")
                    });
                }

                return documents;
            }
            catch (SqlException ex) when (ex.Message.Contains("Invalid column name 'ExcelName'") || ex.Message.Contains("Invalid column name 'ExcelPath'"))
            {
                // If we still get this error, reinitialize the database
                InitializeDatabase();
                
                // Try again after reinitialization
                return await GetDocumentsAsync(filter);
            }
            catch (Exception ex)
            {
                throw new Exception($"Database error while loading documents: {ex.Message}. The database may need to be reinitialized.", ex);
            }
        }

        public async Task<Document> GetDocumentByIdAsync(int id)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                using var command = connection.CreateCommand();
                command.CommandText = @"
                    SELECT 
                        Id,
                        FileName,
                        FilePath,
                        FileType,
                        FileSizeMB,
                        PageCount,
                        UploadedBy,
                        Status,
                        ExcelName,
                        ExcelPath,
                        ExcelSizeMB
                    FROM documents1 
                    WHERE Id = @Id";
                command.Parameters.AddWithValue("@Id", id);

                using var reader = await command.ExecuteReaderAsync();

                if (await reader.ReadAsync())
                {
                    return new Document
                    {
                        Id = reader.GetInt32("Id"),
                        PDFName = reader.GetString("FileName"),
                        PDFPath = reader.GetString("FilePath"),
                        PDFPages = reader.IsDBNull("PageCount") ? null : reader.GetInt32("PageCount"),
                        UploadedBy = reader.GetString("UploadedBy"),
                        FileSizeMB = reader.IsDBNull("FileSizeMB") ? 0 : (float)reader.GetDouble("FileSizeMB"),
                        Status = reader.GetString("Status"),
                        ExcelName = reader.GetString("ExcelName"),
                        ExcelPath =  reader.GetString("ExcelPath"),
                        ExcelSizeMB = reader.GetString("ExcelSizeMB")
                    };
                }

                return null;
            }
            catch (SqlException ex) when (ex.Message.Contains("Invalid column name 'ExcelName'") || ex.Message.Contains("Invalid column name 'ExcelPath'"))
            {
                // If we still get this error, reinitialize the database
                InitializeDatabase();
                
                // Try again after reinitialization
                return await GetDocumentByIdAsync(id);
            }
        }

        public async Task<List<Comment>> GetCommentsForDocumentAsync(int documentId)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT Id, FileId, PageNumber, XPosition, YPosition, CommentText, CreatedBy, CreatedAt 
                FROM Comments 
                WHERE FileId = @DocumentId 
                ORDER BY CreatedAt DESC";
            command.Parameters.AddWithValue("@DocumentId", documentId);

            using var reader = await command.ExecuteReaderAsync();
            var comments = new List<Comment>();

            while (await reader.ReadAsync())
            {
                comments.Add(new Comment
                {
                    Id = reader.GetInt32("Id"),
                    DocumentId = reader.GetInt32("FileId"),
                    PageNumber = reader.GetInt32("PageNumber"),
                    XPosition = Convert.ToSingle(reader.GetDouble("XPosition")),
                    YPosition = Convert.ToSingle(reader.GetDouble("YPosition")),
                    CommentText = reader.GetString("CommentText"),
                    CreatedBy = reader.GetString("CreatedBy"),
                    CreatedAt = reader.GetDateTime("CreatedAt")
                });
            }

            return comments;
        }

        public async Task<int> AddCommentAsync(Comment comment)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO Comments (FileId, PageNumber, XPosition, YPosition, CommentText, CreatedBy)
                VALUES (@FileId, @PageNumber, @XPosition, @YPosition, @CommentText, @CreatedBy);
                SELECT SCOPE_IDENTITY();";

            command.Parameters.AddWithValue("@FileId", comment.DocumentId);
            command.Parameters.AddWithValue("@PageNumber", comment.PageNumber);
            command.Parameters.AddWithValue("@XPosition", comment.XPosition);
            command.Parameters.AddWithValue("@YPosition", comment.YPosition);
            command.Parameters.AddWithValue("@CommentText", comment.CommentText);
            command.Parameters.AddWithValue("@CreatedBy", comment.CreatedBy);

            var result = await command.ExecuteScalarAsync();
            return Convert.ToInt32(result);
        }

        public async Task UpdateDocumentStatusAsync(int documentId, string status)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE documents1 SET Status = @Status WHERE Id = @Id";
            command.Parameters.AddWithValue("@Id", documentId);
            command.Parameters.AddWithValue("@Status", status);

            await command.ExecuteNonQueryAsync();
        }

        public async Task UpdateExcelFileAsync(int documentId, string excelName, string excelPath, float? excelSizeMB = null)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE documents1 SET ExcelName = @ExcelName, ExcelPath = @ExcelPath, ExcelSizeMB = @ExcelSizeMB WHERE Id = @Id";
            command.Parameters.AddWithValue("@Id", documentId);
            command.Parameters.AddWithValue("@ExcelName", excelName);
            command.Parameters.AddWithValue("@ExcelPath", excelPath);
            command.Parameters.AddWithValue("@ExcelSizeMB", (object)excelSizeMB ?? DBNull.Value);

            await command.ExecuteNonQueryAsync();
        }

        public async Task<List<Document>> GetPendingDocumentsAsync()
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT 
                    Id,
                    FileName,
                    FilePath,
                    FileType,
                    FileSizeMB,
                    PageCount,
                    UploadedBy,
                    Status,
                    ExcelName,
                    ExcelPath,
                    ExcelSizeMB
                FROM documents1 
                WHERE Status = 'Pending' 
                ORDER BY Id DESC";

            using var reader = await command.ExecuteReaderAsync();
            var documents = new List<Document>();

            while (await reader.ReadAsync())
            {
                documents.Add(new Document
                {
                    Id = reader.GetInt32("Id"),
                    PDFName = reader.GetString("FileName"),
                    PDFPath = reader.GetString("FilePath"),
                    PDFPages = reader.IsDBNull("PageCount") ? null : reader.GetInt32("PageCount"),
                    UploadedBy = reader.GetString("UploadedBy"),
                    FileSizeMB = reader.IsDBNull("FileSizeMB") ? 0 : (float)reader.GetDouble("FileSizeMB"),
                    Status = reader.GetString("Status"),
                    ExcelName =  reader.GetString("ExcelName"),
                    ExcelPath =  reader.GetString("ExcelPath"),
                    ExcelSizeMB = reader.GetString("ExcelSizeMB")
                });
            }

            return documents;
        }
    }
}