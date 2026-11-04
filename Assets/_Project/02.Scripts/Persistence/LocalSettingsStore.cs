using System;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// 로컬 옵션 JSON 읽기와 후보 파일 교체 저장
/// </summary>
public class LocalSettingsStore
{
    readonly string _path;

    /// <summary>
    /// 로컬 옵션 파일 경로 지정
    /// </summary>
    /// <param name="path">옵션 파일 경로</param>
    public LocalSettingsStore(string path)
    {
        _path = Path.GetFullPath(path);
    }

    /// <summary>
    /// 저장된 옵션 읽기
    /// </summary>
    /// <returns>저장 데이터, 최초 실행이면 null</returns>
    public SettingsData Load()
    {
        string json;
        try
        {
            json = File.ReadAllText(_path, Encoding.UTF8);
        }
        catch (FileNotFoundException)
        {
            if (File.Exists(_path + ".bak"))
                throw new InvalidDataException("옵션 원본 없이 백업만 존재하여 복구 필요");

            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidDataException("옵션 파일 내용이 비어 있음");

        // 누락 필드와 유효한 초기값의 구분
        SettingsData data = new SettingsData { Version = -1, MasterVolume = float.NaN };
        JsonUtility.FromJsonOverwrite(json, data);
        return data;
    }

    /// <summary>
    /// 후보 파일 기록과 옵션 파일 교체
    /// </summary>
    /// <param name="data">검증된 옵션 스냅샷</param>
    public void Save(SettingsData data)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));

        Directory.CreateDirectory(Path.GetDirectoryName(_path));
        string temporaryPath = _path + ".tmp";
        byte[] bytes = new UTF8Encoding(false).GetBytes(JsonUtility.ToJson(data, true));

        using (FileStream stream = new FileStream(
            temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush(true);
        }

        if (File.Exists(_path))
            File.Replace(temporaryPath, _path, _path + ".bak");
        else
            File.Move(temporaryPath, _path);
    }
}
