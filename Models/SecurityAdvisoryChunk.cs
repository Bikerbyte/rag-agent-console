using System.ComponentModel.DataAnnotations;

namespace SecurityAdvisoryBot.Models;

public class SecurityAdvisoryChunk
{
    public int SecurityAdvisoryChunkId { get; set; }
    public int SecurityAdvisoryId { get; set; }

    [MaxLength(32)]
    public required string ChunkKind { get; set; }

    [MaxLength(4000)]
    public required string ChunkText { get; set; }

    public required string EmbeddingJson { get; set; }
    public DateTimeOffset CreatedTime { get; set; }

    public SecurityAdvisory? Advisory { get; set; }
}
