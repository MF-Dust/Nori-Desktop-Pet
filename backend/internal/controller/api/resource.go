package api

import (
	"backend/internal/service"
	"backend/internal/utils"
	"net/http"
)

// GetResourceDownloadURL 获取资源下载链接
func GetResourceDownloadURL() http.HandlerFunc {
	return func(w http.ResponseWriter, r *http.Request) {
		q := utils.NewQueryParser(r)
		resourceType := q.String("type")
		resourceName := q.String("name")

		if resourceType == "" {
			utils.BadRequest(w, "type 不能为空")
			return
		}
		if resourceName == "" {
			utils.BadRequest(w, "name 不能为空")
			return
		}

		// 创建OSS服务
		ossService, err := service.NewOSSService()
		if err != nil {
			utils.InternalServerError(w, "创建OSS服务失败")
			return
		}

		// 获取签名URL
		signedURL, err := ossService.GetSignedURL(resourceType, resourceName)
		if err != nil {
			utils.Error(w, http.StatusNotFound, err.Error())
			return
		}

		utils.Success(w, map[string]any{
			"url": signedURL,
		})
	}
}
