#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/.." && pwd)"
current_root="$(cd "$repo_root/../.." && pwd)"
pinned_root=""
work_root=""
current_validation_build_args=()
pinned_validation_build_args=()
pinned_sotn_commit="c1037ded877f60588a162675fa558415bf6c1995"
pinned_runtime_commit="9c5d7fced450549b6a8874e4fa9a4accae1eb138"

while (($# > 0)); do
    case "$1" in
        --current-root|--pinned-root|--work-root)
            if (($# < 2)); then
                printf 'Missing value for %s\n' "$1" >&2
                exit 2
            fi
            case "$1" in
                --current-root)
                    current_root="$(cd "$2" && pwd)"
                    ;;
                --pinned-root)
                    pinned_root="$(cd "$2" && pwd)"
                    ;;
                --work-root)
                    if [[ ! -d "$2" || ! -w "$2" ]]; then
                        printf 'Work root must be an existing writable directory: %s\n' "$2" >&2
                        exit 2
                    fi
                    work_root="$(cd "$2" && pwd)"
                    ;;
            esac
            shift 2
            ;;
        *)
            printf 'Unknown argument: %s\n' "$1" >&2
            exit 2
            ;;
    esac
done

if [[ -z "$work_root" ]]; then
    work_root="$repo_root/obj"
else
    current_validation_build_args=(
        "-p:BaseOutputPath=$work_root/CoopValidation/current/bin/"
        "-p:BaseIntermediateOutputPath=$work_root/CoopValidation/current/obj/"
    )
    pinned_validation_build_args=(
        "-p:BaseOutputPath=$work_root/CoopValidation/v0.4.3b/bin/"
        "-p:BaseIntermediateOutputPath=$work_root/CoopValidation/v0.4.3b/obj/"
    )
fi

validate_root() {
    local root="$1"
    local label="$2"
    if [[ ! -f "$root/RecompOne/RecompOne.Runtime/RecompOne.Runtime.csproj" || ! -d "$root/wrapers" || ! -d "$root/events" ]]; then
        printf '%s is not a compatible SymphonyRecomp source root.\n' "$root" >&2
        exit 2
    fi
    printf '[CoopValidation] checking %s at %s\n' "$label" "$root"
}

if [[ -z "$pinned_root" ]]; then
    pinned_root="$work_root/compat/v0.4.3b"
    rm -rf "$pinned_root"
    mkdir -p "$pinned_root/RecompOne"
    git -C "$current_root" archive "$pinned_sotn_commit" | tar -x -C "$pinned_root"
    git -C "$current_root/RecompOne" archive "$pinned_runtime_commit" | tar -x -C "$pinned_root/RecompOne"
fi

validate_root "$current_root" "current"
validate_root "$pinned_root" "v0.4.3b"

dotnet run --project "$repo_root/.validation/CoopDiagnostics/CoopDiagnostics.csproj" --configuration Release
dotnet run --project "$repo_root/.validation/CoopRouteManifest/CoopRouteManifest.csproj" --configuration Release
dotnet run --project "$repo_root/.validation/CoopReleaseMatrix/CoopReleaseMatrix.csproj" --configuration Release
dotnet run --project "$repo_root/.validation/CoopTetherPolicy/CoopTetherPolicy.csproj" --configuration Release
dotnet run --project "$repo_root/.validation/CoopNativeDrop/CoopNativeDrop.csproj" --configuration Release
dotnet run --project "$repo_root/.validation/CoopManagedState/CoopManagedState.csproj" --configuration Release
dotnet run --project "$repo_root/.validation/CoopPad2Source/CoopPad2Source.csproj" --configuration Release
dotnet run --project "$repo_root/.validation/CoopManagedHealth/CoopManagedHealth.csproj" --configuration Release
dotnet run --project "$repo_root/.validation/CoopAttackLease/CoopAttackLease.csproj" --configuration Release
dotnet run --project "$repo_root/.validation/CoopAttackLifetime/CoopAttackLifetime.csproj" --configuration Release
dotnet run --project "$repo_root/.validation/CoopAttackPublication/CoopAttackPublication.csproj" --configuration Release
dotnet run --project "$repo_root/.validation/CoopContactOpportunity/CoopContactOpportunity.csproj" --configuration Release
dotnet run --project "$repo_root/.validation/CoopJumpForgiveness/CoopJumpForgiveness.csproj" --configuration Release
dotnet run --project "$repo_root/.validation/CoopManagedStance/CoopManagedStance.csproj" --configuration Release
dotnet run --project "$repo_root/.validation/CoopReconstructionPolicy/CoopReconstructionPolicy.csproj" --configuration Release
dotnet run --project "$repo_root/.validation/CoopMovementSession/CoopMovementSession.csproj" --configuration Release
dotnet run --project "$repo_root/.validation/CoopManagedLocomotion/CoopManagedLocomotion.csproj" --configuration Release
dotnet run --project "$repo_root/.validation/CoopManagedReplayClosure/CoopManagedReplayClosure.csproj" --configuration Release

publish_runtime() {
    local root="$1"
    local label="$2"
    local output="$work_root/runtime/$label"
    rm -rf "$output" "$work_root/runtime-artifacts/$label"
    dotnet publish "$root/RecompOne/RecompOne.Runtime/RecompOne.Runtime.csproj" --configuration Release \
        --artifacts-path "$work_root/runtime-artifacts/$label" --output "$output" --nologo >&2
    printf '%s\n' "$output"
}

current_runtime="$(publish_runtime "$current_root" current)"
pinned_runtime="$(publish_runtime "$pinned_root" v0.4.3b)"

dotnet run --project "$repo_root/.validation/CoopValidation/CoopValidation.Current.csproj" --configuration Release \
    "${current_validation_build_args[@]}" -p:RuntimeReferenceRoot="$current_runtime" -p:SymphonyRecompRoot="$current_root" -- "$repo_root" current "$current_root"
dotnet run --project "$repo_root/.validation/CoopValidation/CoopValidation.Pinned.csproj" --configuration Release \
    "${pinned_validation_build_args[@]}" -p:RuntimeReferenceRoot="$pinned_runtime" -p:SymphonyRecompRoot="$pinned_root" -- "$repo_root" v0.4.3b "$pinned_root"
