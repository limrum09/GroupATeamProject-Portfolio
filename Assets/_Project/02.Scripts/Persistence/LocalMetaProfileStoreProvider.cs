using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

/// <summary>
/// 사용자별 로컬 메타 프로필 저장소 생성
/// </summary>
[DisallowMultipleComponent]
public class LocalMetaProfileStoreProvider : MetaProfileStoreProvider
{
    /// <summary>
    /// 기존 로컬 경로 규칙을 사용하는 저장소 생성
    /// </summary>
    /// <param name="userId">사용자 UID, null이면 기존 로컬 테스트 경로</param>
    /// <returns>로컬 프로필 저장소</returns>
    public override IMetaProfileStore CreateStore(string userId)
    {
        return new LocalMetaProfileStore(GetProfileDirectory(userId));
    }

    /// <summary>
    /// 사용자별 로컬 프로필 디렉터리 조회
    /// </summary>
    /// <param name="userId">사용자 UID, 로컬 테스트 경로는 null</param>
    /// <returns>로컬 프로필 디렉터리</returns>
    public static string GetProfileDirectory(string userId)
    {
        string directoryPath = Path.Combine(Application.persistentDataPath, "Profiles");

        if (userId != null)
        {
            using SHA256 sha256 = SHA256.Create();

            byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(userId));

            string userDirectory = BitConverter.ToString(hash).Replace("-", "");

            directoryPath = Path.Combine(directoryPath, "Users", userDirectory);
        }

        return directoryPath;
    }
}