using System;

/// <summary>
/// 로비에서 확정하여 런 씬으로 전달하는 시작 조건
/// </summary>
public sealed class RunStartRequest
{
    public string CharacterId => GrowthSnapshot.CharacterId;
    public RunGrowthSnapshot GrowthSnapshot { get; }
    public Difficulty SelectedDifficulty { get; }

    /// <summary>
    /// 성장 스냅샷과 선택 난이도로 런 시작 조건 확정
    /// </summary>
    /// <param name="growthSnapshot">선택 캐릭터의 확정된 메타 성장 스냅샷</param>
    /// <param name="selectedDifficulty">이번 런에서 사용할 난이도</param>
    public RunStartRequest(RunGrowthSnapshot growthSnapshot, Difficulty selectedDifficulty)
    {
        if (growthSnapshot == null)
            throw new ArgumentNullException(nameof(growthSnapshot));

        ValidateDifficulty(selectedDifficulty);

        GrowthSnapshot = growthSnapshot;
        SelectedDifficulty = selectedDifficulty;
    }

    /// <summary>
    /// 실제 선택 가능한 난이도 값 검사
    /// </summary>
    /// <param name="difficulty">검사할 난이도</param>
    public static void ValidateDifficulty(Difficulty difficulty)
    {
        if (!Enum.IsDefined(typeof(Difficulty), difficulty) || difficulty == Difficulty.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(difficulty), "Easy, Normal, Hard, Nightmare 중 선택 필요");
        }
    }
}