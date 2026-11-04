using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// 사용자 로컬 스토리 해금 기록의 읽기 및 원자적 저장
/// </summary>
public class LocalStoryUnlockStore
{
    readonly string _directory;
    readonly string _path;
    readonly object _gate = new object();
    HashSet<(string CharacterId, string StoryId)> _records =
        new HashSet<(string CharacterId, string StoryId)>();

    public string LoadError { get; }

    /// <summary>
    /// 사용자 디렉터리에서 스토리 해금 기록 로드
    /// </summary>
    /// <param name="directory">로컬 프로필 디렉터리</param>
    public LocalStoryUnlockStore(string directory)
    {
        _directory = directory;
        _path = Path.Combine(directory, "story-unlocks.json");

        try
        {
            _records = ReadRecords();
        }
        catch (Exception exception) when (exception is IOException ||
            exception is UnauthorizedAccessException || exception is JsonException)
        {
            LoadError = "로컬 스토리 해금 기록을 불러오지 못했습니다.";
        }
    }

    HashSet<(string CharacterId, string StoryId)> ReadRecords()
    {
        string json;
        try
        {
            json = File.ReadAllText(_path);
        }
        catch (FileNotFoundException)
        {
            return _records;
        }
        catch (DirectoryNotFoundException)
        {
            return _records;
        }

        HashSet<(string CharacterId, string StoryId)> records =
            new HashSet<(string CharacterId, string StoryId)>();
        foreach (JToken token in JArray.Parse(json))
        {
            if (!(token is JObject row) ||
                row["characterId"]?.Type != JTokenType.String ||
                row["storyId"]?.Type != JTokenType.String)
            {
                throw new JsonSerializationException("스토리 해금 기록 형식 오류");
            }

            string characterId = (string)row["characterId"];
            string storyId = (string)row["storyId"];
            if (string.IsNullOrWhiteSpace(characterId) || string.IsNullOrWhiteSpace(storyId))
                throw new JsonSerializationException("스토리 해금 기록 식별자 필요");

            records.Add((characterId, storyId));
        }

        return records;
    }

    /// <summary>
    /// 캐릭터별 스토리 해금 완료 기록 조회
    /// </summary>
    /// <param name="characterId">캐릭터 식별자</param>
    /// <param name="storyId">스토리 식별자</param>
    /// <returns>해금 완료 기록 존재 여부</returns>
    public bool Contains(string characterId, string storyId)
    {
        lock (_gate)
            return _records.Contains((characterId, storyId));
    }

    /// <summary>
    /// 해금 기록을 디스크에 커밋한 뒤 메모리에 반영
    /// </summary>
    /// <param name="characterId">캐릭터 식별자</param>
    /// <param name="storyId">스토리 식별자</param>
    public void SaveUnlock(string characterId, string storyId)
    {
        lock (_gate)
        {
            if (LoadError != null)
                throw new IOException(LoadError);

            if (_records.Contains((characterId, storyId))) return;

            var next = new HashSet<(string CharacterId, string StoryId)>(_records)
            {
                (characterId, storyId)
            };

            JArray rows = new JArray();
            foreach ((string character, string story) in next)
                rows.Add(new JObject { ["characterId"] = character, ["storyId"] = story });

            Directory.CreateDirectory(_directory);
            string temporaryPath = _path + ".tmp";
            using (FileStream stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                byte[] bytes = System.Text.Encoding.UTF8.GetBytes(rows.ToString(Formatting.None));
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }

            if (File.Exists(_path))
                File.Replace(temporaryPath, _path, null);
            else
                File.Move(temporaryPath, _path);

            _records = next;
        }
    }
}
