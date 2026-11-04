using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// 로컬 메타 프로필의 직렬화와 파일 읽기 및 저장
/// </summary>
public class LocalMetaProfileStore : IMetaProfileStore
{
    readonly string _directoryPath;
    readonly string _profilePath;
    readonly string _temporaryPath;
    readonly string _backupPath;
    readonly string _legacyBackupPath;

    readonly SemaphoreSlim _ioGate = new SemaphoreSlim(1, 1);

    JObject _committed;
    bool _canSave;
    bool _hasCommittedProfile;
    bool _needsLegacyBackup;

    static bool PathExists(string path)
    {
        try
        {
            File.GetAttributes(path);
            return true;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
    }

    static void RequireType(JObject source, string propertyName, JTokenType expectedType)
    {
        JToken token = source[propertyName];

        if (token == null || token.Type != expectedType)
            throw new JsonSerializationException($"{propertyName}: {expectedType} 형식 필요");
    }

    static void RequireProgressValue(JObject row)
    {
        JToken token = row["currentValue"];

        if (token == null || (token.Type != JTokenType.Integer && token.Type != JTokenType.Float))
            throw new JsonSerializationException("퀘스트 진행값은 숫자 필요");

        float value = token.Value<float>();

        if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
            throw new JsonSerializationException("유효하지 않은 퀘스트 진행값");
    }

    static bool UpgradeLegacyProfile(JObject document)
    {
        if (document.Property("dailyQuests") != null)
            return false;

        RequireType(document, "operationId", JTokenType.String);

        if (!Guid.TryParse(document.Value<string>("operationId"), out Guid operationId) || operationId == Guid.Empty)
            throw new JsonSerializationException("구형 저장 작업 ID 오류");

        if (document.Property("quests") == null)
            document["quests"] = new JArray();

        RequireType(document, "quests", JTokenType.Array);

        List<JObject> records = new List<JObject>();
        string periodKey = "D:manual-initial";

        foreach (JToken item in (JArray)document["quests"])
        {
            if (!(item is JObject row))
                throw new JsonSerializationException("구형 퀘스트 항목 오류");

            RequireType(row, "questId", JTokenType.String);
            RequireType(row, "periodKey", JTokenType.String);
            RequireType(row, "kind", JTokenType.Integer);
            RequireType(row, "displayName", JTokenType.String);
            RequireType(row, "metaGoldReward", JTokenType.Integer);
            RequireType(row, "isCompleted", JTokenType.Boolean);
            RequireType(row, "isClaimed", JTokenType.Boolean);
            RequireProgressValue(row);

            QuestKind kind = (QuestKind)row.Value<int>("kind");

            // 기존 기록 생성자의 기본 검증 유지
            _ = new QuestRecord(row.Value<string>("questId"), row.Value<string>("periodKey"), kind, row.Value<string>("displayName"), row.Value<float>("currentValue"), row.Value<bool>("isCompleted"), row.Value<bool>("isClaimed"), row.Value<long>("metaGoldReward"));

            records.Add(row);

            if (kind == QuestKind.Daily)
                periodKey = row.Value<string>("periodKey");
        }

        // 기존 복원과 동일한 현재 주기 및 앞의 네 개 선택
        JArray entries = new JArray();

        foreach (JObject row in records.Where(row => row.Value<int>("kind") == (int)QuestKind.Daily && row.Value<string>("periodKey") == periodKey).Take(4))
        {
            entries.Add(new JObject
            {
                ["questId"] = row["questId"].DeepClone(),
                ["currentValue"] = row["currentValue"].DeepClone(),
                ["isCompleted"] = row["isCompleted"].DeepClone(),
                ["isClaimed"] = row["isClaimed"].DeepClone()
            });
        }

        document.Remove("operationId");
        document.Remove("quests");

        document["dailyQuests"] = new JObject
        {
            ["periodKey"] = periodKey,
            ["entries"] = entries
        };

        return true;
    }

    static MetaProfileLoadResult ParseProfile(string json)
    {
        JObject document;

        using (StringReader textReader = new StringReader(json))

        using (JsonTextReader reader = new JsonTextReader(textReader))
        {
            reader.DateParseHandling = DateParseHandling.None;

            JsonLoadSettings settings = new JsonLoadSettings
            {
                DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error
            };

            document = JObject.Load(reader, settings);

            if (reader.Read())
                throw new JsonSerializationException("JSON 루트 뒤 추가 내용 존재");
        }

        RequireType(document, "metaGold", JTokenType.Integer);
        RequireType(document, "purchases", JTokenType.Array);

        foreach (JToken item in (JArray)document["purchases"])
        {
            if (!(item is JObject row))
                throw new JsonSerializationException("구매 항목은 JSON 객체 필요");

            RequireType(row, "characterId", JTokenType.String);
            RequireType(row, "upgradeId", JTokenType.String);
            RequireType(row, "rank", JTokenType.Integer);
        }

        RequireType(document, "loadouts", JTokenType.Array);

        foreach (JToken item in (JArray)document["loadouts"])
        {
            if (!(item is JObject row))
                throw new JsonSerializationException("장착 기록은 JSON 객체 필요");

            RequireType(row, "characterId", JTokenType.String);
            RequireType(row, "slots", JTokenType.Array);

            foreach (JToken slot in (JArray)row["slots"])
            {
                if (slot.Type != JTokenType.Null && slot.Type != JTokenType.String)
                    throw new JsonSerializationException("슬롯 값은 강화 식별자 또는 null 필요");
            }
        }

        RequireType(document, "mastery", JTokenType.Array);

        foreach (JToken item in (JArray)document["mastery"])
        {
            if (!(item is JObject row))
                throw new JsonSerializationException("숙련도 기록은 JSON 객체 필요");

            RequireType(row, "characterId", JTokenType.String);
            RequireType(row, "totalExperience", JTokenType.Integer);
        }

        bool migrated = UpgradeLegacyProfile(document);

        RequireType(document, "dailyQuests", JTokenType.Object);

        JObject daily = (JObject)document["dailyQuests"];

        RequireType(daily, "periodKey", JTokenType.String);
        RequireType(daily, "entries", JTokenType.Array);

        if (string.IsNullOrWhiteSpace(daily.Value<string>("periodKey")))
            throw new JsonSerializationException("일일 주기 필요");

        JArray entries = (JArray)daily["entries"];

        if (entries.Count > 4)
            throw new JsonSerializationException("일일 퀘스트 개수 초과");

        HashSet<string> questIds =
            new HashSet<string>(StringComparer.Ordinal);

        foreach (JToken item in entries)
        {
            if (!(item is JObject row))
                throw new JsonSerializationException("퀘스트 항목은 객체 필요");

            RequireType(row, "questId", JTokenType.String);
            RequireType(row, "isCompleted", JTokenType.Boolean);
            RequireType(row, "isClaimed", JTokenType.Boolean);
            RequireProgressValue(row);

            string questId = row.Value<string>("questId");

            if (string.IsNullOrWhiteSpace(questId) || !questIds.Add(questId))
                throw new JsonSerializationException("빈 ID 또는 중복 퀘스트");

            if (row.Value<bool>("isClaimed") && !row.Value<bool>("isCompleted"))
                throw new JsonSerializationException("미완료 퀘스트의 수령 기록");
        }

        JsonSerializer serializer = new JsonSerializer
        {
            TypeNameHandling = TypeNameHandling.None,
            MissingMemberHandling = MissingMemberHandling.Error
        };

        MetaProfileData data = document.ToObject<MetaProfileData>(serializer);

        if (data.MetaGold < 0)
            throw new JsonSerializationException("음수 메타 재화");

        // 형식 검증 이후 기능별 기본 기록 계약 검증
        foreach (MetaProfileData.PurchaseData row in data.Purchases)
            _ = new MetaUpgradeRecord(row.CharacterId, row.UpgradeId, row.Rank);

        foreach (MetaProfileData.LoadoutData row in data.Loadouts)
            _ = new MetaLoadoutRecord(row.CharacterId, row.Slots);

        foreach (MetaProfileData.MasteryData row in data.Mastery)
            _ = new MetaMasteryRecord(row.CharacterId, row.TotalExperience);

        return new MetaProfileLoadResult(MetaProfileLoadStatus.Loaded, data, requiresMigration: migrated);
    }

    MetaProfileLoadResult ReadProfile()
    {
        try
        {
            if (!PathExists(_profilePath))
            {
                if (PathExists(_temporaryPath) || PathExists(_backupPath))
                    return new MetaProfileLoadResult(MetaProfileLoadStatus.RecoveryRequired, message: "본파일 없이 임시 또는 백업 파일이 남아 있어 복구 필요");

                return new MetaProfileLoadResult(MetaProfileLoadStatus.Missing);
            }

            string json = File.ReadAllText(_profilePath, Encoding.UTF8);

            return ParseProfile(json);
        }
        catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is System.Security.SecurityException)
        {
            return new MetaProfileLoadResult(MetaProfileLoadStatus.ReadFailed, message: exception.Message);
        }
        catch (Exception exception) when (exception is JsonException || exception is FormatException || exception is ArgumentException || exception is OverflowException)
        {
            return new MetaProfileLoadResult(MetaProfileLoadStatus.InvalidData, message: exception.Message);
        }
    }

    void WriteProfile(JObject document)
    {
        Directory.CreateDirectory(_directoryPath);

        // 최초 형식 이전 시 원본을 별도 보존
        if (_needsLegacyBackup && !PathExists(_legacyBackupPath))
            File.Copy(_profilePath, _legacyBackupPath, false);

        byte[] bytes = Encoding.UTF8.GetBytes(
            document.ToString(Formatting.Indented));

        using (FileStream stream = new FileStream(_temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush(true);
        }

        if (_hasCommittedProfile)
        {
            File.Replace(_temporaryPath, _profilePath, _backupPath);
        }
        else
        {
            File.Move(_temporaryPath, _profilePath);
        }

        _hasCommittedProfile = true;
    }

    /// <summary>
    /// 지정한 절대 경로에 로컬 프로필 저장소 구성
    /// </summary>
    /// <param name="directoryPath">프로필 파일을 보관할 절대 폴더 경로</param>
    public LocalMetaProfileStore(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || !Path.IsPathRooted(directoryPath))
        {
            throw new ArgumentException("프로필 폴더의 절대 경로 필요", nameof(directoryPath));
        }

        _directoryPath = Path.GetFullPath(directoryPath);
        _profilePath = Path.Combine(_directoryPath, "meta-profile.json");

        _temporaryPath = _profilePath + ".tmp";
        _backupPath = _profilePath + ".bak";
        _legacyBackupPath = _profilePath + ".legacy.bak";
    }

    /// <summary>
    /// 공통 프로필 읽기와 마지막 저장본 보관
    /// </summary>
    /// <returns>프로필 또는 파일 없음과 오류를 구분한 결과</returns>
    public async Task<MetaProfileLoadResult> LoadAsync()
    {
        await _ioGate.WaitAsync().ConfigureAwait(false);

        try
        {
            _canSave = false;

            MetaProfileLoadResult result = await Task.Run(ReadProfile).ConfigureAwait(false);

            _hasCommittedProfile = result.Status == MetaProfileLoadStatus.Loaded;

            _committed = _hasCommittedProfile ? JObject.FromObject(result.Data, new JsonSerializer()) : null;

            _needsLegacyBackup = _hasCommittedProfile && result.RequiresMigration;

            _canSave = _hasCommittedProfile || result.Status == MetaProfileLoadStatus.Missing;

            return result;
        }
        finally
        {
            _ioGate.Release();
        }
    }

    /// <summary>
    /// 마지막 저장본에 지정된 변경만 반영하여 파일 기록
    /// </summary>
    /// <param name="changes">이번 저장에 고정된 변경 데이터</param>
    /// <returns>본파일 교체 성공 여부</returns>
    public async Task<bool> SaveAsync(MetaSaveChanges changes)
    {
        if (changes == null)
            throw new ArgumentNullException(nameof(changes));

        await _ioGate.WaitAsync().ConfigureAwait(false);

        try
        {
            if (!_canSave)
                throw new InvalidOperationException("프로필 읽기 완료 필요");

            if (changes.Parts == MetaSavePart.None)
                return true;

            if (_committed == null && changes.Parts != MetaSavePart.All)
                throw new InvalidOperationException("최초 저장은 모든 항목 필요");

            JObject candidate = _committed != null ? (JObject)_committed.DeepClone() : new JObject();

            foreach (KeyValuePair<string, JToken> field in changes.CopyFields())
            {
                candidate[field.Key] = field.Value;
            }

            // 쓰기 전에 완성된 저장본의 형식 확인
            ParseProfile(candidate.ToString(Formatting.None));

            await Task.Run(() => WriteProfile(candidate)).ConfigureAwait(false);

            // 파일 기록 성공 이후에만 기준 저장본 교체
            _committed = candidate;
            _needsLegacyBackup = false;

            return true;
        }
        finally
        {
            _ioGate.Release();
        }
    }
}