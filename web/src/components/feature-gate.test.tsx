import { render, screen } from "@testing-library/react";
import { I18nextProvider } from "react-i18next";
import { describe, expect, it } from "vitest";
import i18n from "@/i18n";
import type { FeatureStatus } from "@/lib/types";
import { FeatureGate, StaticFeatureProvider } from "./feature-gate";

function row(partial: Partial<FeatureStatus> & Pick<FeatureStatus, "featureKey">): FeatureStatus {
  return {
    isInPlan: true,
    isOverridden: false,
    isTenantEnabled: true,
    isEffectiveEnabled: true,
    updatedAt: null,
    ...partial,
  };
}

function renderGate(features: FeatureStatus[], fallback?: string) {
  return render(
    <I18nextProvider i18n={i18n}>
      <StaticFeatureProvider features={features}>
        <FeatureGate feature="ParentPortal" fallback={fallback ? <p>{fallback}</p> : undefined}>
          <p>Portal content</p>
        </FeatureGate>
      </StaticFeatureProvider>
    </I18nextProvider>,
  );
}

describe("FeatureGate", () => {
  it("renders children when the feature is on", () => {
    renderGate([row({ featureKey: "ParentPortal" })]);
    expect(screen.getByText("Portal content")).toBeInTheDocument();
  });

  it("renders the fallback when the institute switch is off", () => {
    renderGate(
      [row({ featureKey: "ParentPortal", isTenantEnabled: false, isEffectiveEnabled: false })],
      "Turned off",
    );
    expect(screen.getByText("Turned off")).toBeInTheDocument();
    expect(screen.queryByText("Portal content")).not.toBeInTheDocument();
  });

  it("asks for a plan upgrade when the feature is not included", () => {
    renderGate([
      row({
        featureKey: "ParentPortal",
        isInPlan: false,
        isTenantEnabled: false,
        isEffectiveEnabled: false,
      }),
    ]);
    expect(screen.getByText("Upgrade plan to use this.")).toBeInTheDocument();
  });
});
