# 스마트 스토어 알림 (Windows WPF)

네이버 스마트 스토어 URL을 입력하면 새 상품이 나타날 때 Windows 토스트로 알려주는 데스크톱 앱입니다.

## 기능
- 스토어 URL 주기적 크롤링
- 새 상품 식별(상품 ID 기준) 후 Windows 토스트 알림
- URL 직접 열기 버튼
- 앱 데이터에 이미 본 상품 ID를 보관하여 중복 알림 방지
- HTML 정적 파싱 실패 시 WebView2 렌더링 모드로 보조 파싱

## 빌드 방법
1. **필수**: Windows 10/11 + .NET 8 SDK + Visual Studio 2022 이상(Workload: .NET Desktop development).
2. 저장 후 `SmartStoreWatcher.sln` 솔루션을 Visual Studio로 열기.
3. NuGet 패키지 자동 복원 확인:
   - CommunityToolkit.WinUI.Notifications
   - HtmlAgilityPack
   - Microsoft.Web.WebView2
4. 실행(F5) 또는 빌드 후 배포.

## 사용법
1. 앱 실행.
2. 스마트 스토어의 메인 또는 전체상품 URL을 입력.
3. 폴링 주기를 분 단위로 지정(기본 3분).
4. 페이지가 **JS로만 렌더링**되는 경우 `렌더링 모드(WebView2)` 체크.
5. [시작] 버튼. 처음 1회 즉시 확인 후 주기적으로 점검.
6. 새 상품이 탐지되면 **토스트 알림**과 함께 [열기] 버튼으로 브라우저에서 상품 페이지를 엽니다.

> 참고: 토스트 알림은 `CommunityToolkit.WinUI.Notifications`의 **ProtocolActivation**을 사용하므로 별도의 COM Activator 설정 없이 버튼 클릭 시 바로 URL이 열립니다.

## 동작 원리
- 페이지의 `<a href=".../products/{id}">` 링크를 수집하여 `id`를 추출합니다.
- 로컬 `%AppData%\SmartStoreWatcher\state\{storeSlug}.json`에 본 상품 ID를 저장합니다.
- 새 ID가 나오면 알림을 보냅니다.

## 제한 사항
- 일부 스토어는 콘텐츠가 JS로 늦게 붙거나 비표준 마크업을 사용합니다. 이때는 `렌더링 모드(WebView2)`를 켜세요.
- 네이버 정책 변경이나 무한 스크롤 페이지에서는 추가 보완이 필요할 수 있습니다.
- 웹 사이트 이용약관과 로봇 배제정책을 준수하세요. 요청 빈도를 과도하게 높이지 마세요.

## 배포 팁
- `Release` 빌드 후 `publish`를 사용해 단일 폴더 배포 가능:
  ```powershell
  dotnet publish -c Release -r win-x64 --self-contained false
  ```
- 알림이 시스템에서 보이지 않으면 **포커스 지원(집중모드)** 설정을 확인하세요.

## 문의 포인트
- 특정 스토어에서 파싱이 되지 않으면 샘플 URL을 알려주면 선택자 보완이 가능합니다.
