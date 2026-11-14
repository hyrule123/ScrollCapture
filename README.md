# ScrollCapture

.NET Framework 4.8 기반의 **가볍고 설치가 필요 없는** Windows용 스크롤 캡처 도구입니다.
실행 파일 하나(약 60KB)로 동작하며, 외부 라이브러리 의존성이 없습니다.

## 기능

- **영역 선택**
  - 드래그: 원하는 사각형 영역 선택
  - 클릭: 마우스 아래의 창/컨트롤(예: 브라우저 본문 영역) 자동 선택
  - Esc / 우클릭: 취소
- **자동 스크롤 모드**: 마우스 휠 입력을 보내 끝까지 스크롤하며 캡처하고, 더 이상 화면이 바뀌지 않으면 자동으로 종료
- **직접 스크롤 모드**: 휠 입력이 먹히지 않는 프로그램용. 사용자가 직접 스크롤하는 동안 주기적으로 캡처
- **자동 이어 붙이기**: 프레임 간 겹치는 부분을 찾아 정확히 이어 붙임
  - 고정 헤더/푸터(sticky)는 결과에 한 번만 포함
  - 오른쪽 스크롤바의 변화는 비교에서 제외
- **결과 창**: 긴 이미지 미리보기(창 너비 맞춤 / 100%), PNG·JPEG·BMP 저장, 클립보드 복사
- 멀티 모니터 및 고해상도(DPI) 환경 지원 (Per-Monitor V2)
- 캡처 중 Esc 또는 [완료] 버튼으로 언제든 종료

## 요구 사항

- Windows 7 SP1 이상 (Windows 10/11 권장)
- .NET Framework 4.8 (Windows 10 1903 이상에는 기본 포함)

## 사용법

1. `ScrollCapture.exe` 실행
2. **스크롤 캡처 시작** (Ctrl+N) 클릭
3. 스크롤할 영역을 드래그하거나, 창/컨트롤을 클릭해서 선택
4. 자동 모드라면 끝까지 스크롤되며 캡처됩니다. 중간에 멈추려면 Esc
5. 결과 창에서 저장(Ctrl+S) 또는 복사(Ctrl+C)

### 옵션

| 항목 | 설명 | 기본값 |
| --- | --- | --- |
| 모드 | 자동 스크롤 / 직접 스크롤 | 자동 |
| 스크롤 양 (휠 칸) | 한 번에 보내는 휠 칸 수. 이어 붙이기가 실패하면 줄이세요 | 3 |
| 대기 시간 (ms) | 스크롤 후 캡처까지 대기 시간. 부드러운 스크롤/지연 로딩이 있으면 늘리세요 | 300 |
| 캡처 간격 (ms) | 직접 스크롤 모드에서 캡처 주기 | 100 |
| 최대 높이 (px) | 결과 이미지 높이 제한 | 30,000 |

### 설정 파일 (ScrollCapture.ini)

설정은 **실행 파일과 같은 폴더**의 `ScrollCapture.ini`에 저장됩니다 (캡처 시작 시, 프로그램 종료 시 자동 저장).
파일을 지우면 기본값으로 돌아가며, 메모장으로 직접 수정해도 됩니다.

```ini
[Capture]
Mode=Auto              ; Auto = 자동 스크롤, Manual = 직접 스크롤
WheelNotches=3
AutoDelayMs=300        ; 자동 모드 대기 시간
ManualIntervalMs=100   ; 직접 스크롤 모드 캡처 간격
MaxHeight=30000

[MainWindow]
X=...                  ; 마지막 창 위치
Y=...

[ResultWindow]
Width=...              ; 마지막 결과 창 크기
Height=...
Maximized=0
FitWidth=1             ; 창 너비에 맞춤

[Save]
LastDirectory=...      ; 마지막 저장 폴더
Format=PNG             ; PNG, JPEG, BMP (마지막으로 저장한 형식)
JpegQuality=92         ; 1~100
```

> `C:\Program Files` 처럼 쓰기 권한이 없는 폴더에 두면 설정을 저장할 수 없습니다 (경고가 한 번 표시됨).
> 바탕화면, 문서 등 일반 폴더에 두고 사용하세요.

### 팁

- 스크롤바나 움직이는 요소(광고, 애니메이션)는 가급적 선택 영역에서 빼면 더 정확합니다.
- 결과 정보에 "겹침 탐지 실패"가 표시되면 스크롤 양을 줄이거나 대기 시간을 늘려 보세요.
- 화면 높이보다 긴 완전한 빈 여백은 길이가 줄어들 수 있습니다 (내용이 없어 이동량을 알 수 없기 때문).

## 빌드

Visual Studio 2019/2022에서 `ScrollCapture.sln`을 열어 빌드하거나, .NET SDK로 빌드합니다.

```bash
dotnet build src/ScrollCapture/ScrollCapture.csproj -c Release
```

결과물: `src/ScrollCapture/bin/Release/ScrollCapture.exe`

이어 붙이기 알고리즘 테스트:

```bash
dotnet run --project tests/StitcherTests -c Release
```

GitHub Actions가 `main` 푸시마다 exe를 빌드해 Artifacts로 올리고, `v*` 태그를 푸시하면 Release에 첨부합니다.

## 구조

```
src/ScrollCapture/
  Program.cs            진입점, DPI 설정
  AppSettings.cs        ScrollCapture.ini 읽기/쓰기
  MainForm.cs           메인 창 (옵션, 시작 버튼)
  RegionSelectForm.cs   영역/창 선택 오버레이
  CaptureSession.cs     스크롤 + 캡처 루프
  OverlayForms.cs       캡처 영역 테두리, 진행 상태 창 (Esc 전역 단축키)
  Stitcher.cs           프레임 이어 붙이기 알고리즘 (행 해시 기반)
  StitcherBitmap.cs     Stitcher ↔ Bitmap 변환
  ImageViewer.cs        초장 이미지 뷰어
  ResultForm.cs         결과 창 (저장/복사)
  ScreenCapture.cs      화면 캡처, 창 탐색
  NativeMethods.cs      Win32 P/Invoke
tests/StitcherTests/    알고리즘 시뮬레이션 테스트
```

### 이어 붙이기 알고리즘

1. 각 프레임의 행마다 해시를 계산합니다 (오른쪽 스크롤바 영역 제외).
2. 이전 프레임과 같은 위치에서 그대로인 위/아래 행을 고정 헤더/푸터로 보고 비교에서 제외합니다.
3. 가운데 영역에서 `새 프레임[r] == 이전 프레임[r + dy]`가 가장 잘 성립하는 이동량 `dy`를 찾습니다.
   단색 행은 어디에나 일치하므로 판단 근거에서 제외하고, 점수가 비슷하면 직전 이동량에 가까운 값을 고릅니다.
4. 새로 드러난 행만 결과에 덧붙이고, 마지막 프레임의 푸터는 끝에 한 번만 붙입니다.
